using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// 向量類型枚舉
    /// </summary>
    public enum VectorType
    {
        Memory,       // 記憶向量 → VectorDatabase
        ContextDef,   // Context Def 向量 → SemanticCache._defCache
        ContextText   // Context Text 向量 → SemanticCache._textCache
    }

    /// <summary>
    /// 向量計算佇列服務 (Singleton)
    /// </summary>
    public class VectorQueueService
    {
        // --- Singleton ---
        private static VectorQueueService _instance;
        private static readonly object _lock = new();
        public static VectorQueueService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock) { _instance ??= new VectorQueueService(); }
                }
                return _instance;
            }
        }

        // --- 佇列與去重 ---
        private readonly ConcurrentQueue<VectorRequest> _queue = new();
        private readonly ConcurrentDictionary<Guid, byte> _pendingIds = new();

        // --- 處理器控制 ---
        private CancellationTokenSource _cts = new();
        private Task _processorTask = Task.CompletedTask;
        private Task _loadingTask = Task.CompletedTask;

        // --- 雲端流量控制 ---
        private DateTime _cooldownUntil = DateTime.MinValue;
        private const int RETRY_INTERVAL_MS = 5000;
        private const int MAX_RETRIES = 3;
        private const int COOLDOWN_SECONDS = 60;
        private const int BATCH_INTERVAL_MS = 2000;

        private VectorQueueService() { }

        // === 公開 API（方法重載）===

        /// <summary>
        /// 記憶向量入口（可選：複製給多個 ID）
        /// </summary>
        public void Enqueue(Guid memoryId, string text, List<Guid> copyToIds = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (VectorDatabase.Instance.GetVector(memoryId) != null) return;
            if (!_pendingIds.TryAdd(memoryId, 0)) return;

            _queue.Enqueue(new VectorRequest
            {
                Type = VectorType.Memory,
                MemoryId = memoryId,
                Text = text,
                CopyToIds = copyToIds
            });
        }

        /// <summary>
        /// Context 向量入口（Def 或 Text）
        /// </summary>
        public void Enqueue(VectorType type, int contextKey, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (type == VectorType.Memory)
                throw new ArgumentException("Use Enqueue(Guid, string) for Memory type");

            if (SemanticCache.Instance.HasCachedVector(type, contextKey)) return;

            var dedupeId = new Guid(contextKey, 0, 0, new byte[8]);
            if (!_pendingIds.TryAdd(dedupeId, 0)) return;

            _queue.Enqueue(new VectorRequest
            {
                Type = type,
                ContextKey = contextKey,
                Text = text
            });
        }

        /// <summary>
        /// 模式切換處理
        /// </summary>
        public void OnModeChanged(bool toCloud)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();

            if (toCloud)
            {
                VectorService.Instance.UnloadLocal();
                _processorTask = CloudProcessorAsync(_cts.Token);
                Log.Message("[RimTalk] VectorQueue: 切換到雲端模式");
            }
            else
            {
                _loadingTask = VectorService.Instance.LoadLocalAsync();
                _processorTask = LocalProcessorAsync(_cts.Token);
                Log.Message("[RimTalk] VectorQueue: 切換到本地模式");
            }
        }

        /// <summary>
        /// 清空佇列
        /// </summary>
        public void Clear()
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            while (_queue.TryDequeue(out _)) { }
            _pendingIds.Clear();
        }

        // === 雲端處理器 ===
        private async Task CloudProcessorAsync(CancellationToken ct)
        {
            var batch = new List<VectorRequest>();  // 移到外層

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (DateTime.Now < _cooldownUntil)
                        {
                            await Task.Delay(1000, ct);
                            continue;
                        }

                        batch.Clear();  // 重用 batch
                        var deadline = DateTime.Now.AddMilliseconds(BATCH_INTERVAL_MS);

                        while (DateTime.Now < deadline)
                        {
                            if (ct.IsCancellationRequested) break;

                            if (_queue.TryDequeue(out var req))
                            {
                                batch.Add(req);
                                if (req.Type == VectorType.Memory)
                                    _pendingIds.TryRemove(req.MemoryId, out _);
                                else
                                    _pendingIds.TryRemove(new Guid(req.ContextKey, 0, 0, new byte[8]), out _);
                            }
                            else
                            {
                                await Task.Delay(100, ct);
                            }
                        }

                        if (batch.Count == 0) continue;
                        if (ct.IsCancellationRequested) break;  // 檢查取消

                        var vectors = await CallCloudWithRetry(batch, ct);

                        for (int i = 0; i < batch.Count && i < vectors.Count; i++)
                        {
                            if (vectors[i] == null) continue;
                            var req = batch[i];

                            switch (req.Type)
                            {
                                case VectorType.Memory:
                                    VectorDatabase.Instance.AddVector(req.MemoryId, vectors[i]);
                                    // [NEW] 複製給其他 ID
                                    if (req.CopyToIds != null)
                                    {
                                        foreach (var copyId in req.CopyToIds)
                                            VectorDatabase.Instance.AddVector(copyId, vectors[i]);
                                    }
                                    break;
                                case VectorType.ContextDef:
                                case VectorType.ContextText:
                                    SemanticCache.Instance.AddToCache(req.Type, req.ContextKey, vectors[i]);
                                    break;
                            }
                        }

                        batch.Clear();  // 處理完成，清空
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Log.Error($"[RimTalk] CloudProcessor error: {ex.Message}");
                        await Task.Delay(1000, ct);
                    }
                }
            }
            finally
            {
                // [NEW] 取消時將未處理的 batch 放回佇列
                if (batch.Count > 0)
                {
                    Log.Message($"[RimTalk] 模式切換，{batch.Count} 個任務放回佇列");
                    foreach (var req in batch)
                    {
                        _queue.Enqueue(req);
                        if (req.Type == VectorType.Memory)
                            _pendingIds.TryAdd(req.MemoryId, 0);
                        else
                            _pendingIds.TryAdd(new Guid(req.ContextKey, 0, 0, new byte[8]), 0);
                    }
                }
            }
        }

        private async Task<List<float[]>> CallCloudWithRetry(List<VectorRequest> batch, CancellationToken ct)
        {
            int attempt = 0;

            while (attempt < MAX_RETRIES && !ct.IsCancellationRequested)
            {
                try
                {
                    var texts = batch.Select(r => r.Text).ToList();
                    var result = await CloudVectorClient.Instance.ComputeEmbeddingsBatchAsync(texts); // 直接呼叫，沒有檢查是否已快取，有機率重複計算

                    if (result != null && result.Count > 0)
                        return result;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimTalk] 雲端向量計算失敗 (嘗試 {attempt + 1}/{MAX_RETRIES}): {ex.Message}");
                }

                attempt++;

                if (attempt < MAX_RETRIES)
                {
                    Log.Message($"[RimTalk] {RETRY_INTERVAL_MS / 1000}秒後重試...");
                    await Task.Delay(RETRY_INTERVAL_MS, ct);
                }
            }

            _cooldownUntil = DateTime.Now.AddSeconds(COOLDOWN_SECONDS);
            Log.Warning($"[RimTalk] 雲端向量服務進入冷卻期 {COOLDOWN_SECONDS} 秒");

            // 失敗的請求重新入佇列
            foreach (var req in batch)
            {
                if (req.Type == VectorType.Memory)
                {
                    _pendingIds.TryAdd(req.MemoryId, 0);
                }
                else
                {
                    _pendingIds.TryAdd(new Guid(req.ContextKey, 0, 0, new byte[8]), 0);
                }
                _queue.Enqueue(req);
            }

            return new List<float[]>();
        }

        // === 本地處理器 ===

        private async Task LocalProcessorAsync(CancellationToken ct)
        {
            try
            {
                await _loadingTask;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] 本地模型載入失敗: {ex.Message}");
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_queue.TryDequeue(out var req))
                    {
                        // 移除去重 ID
                        if (req.Type == VectorType.Memory)
                            _pendingIds.TryRemove(req.MemoryId, out _);
                        else
                            _pendingIds.TryRemove(new Guid(req.ContextKey, 0, 0, new byte[8]), out _);
                        var vector = VectorService.Instance.LocalComputeEmbedding(req.Text, false);
                        if (vector != null)
                        {
                            switch (req.Type)
                            {
                                case VectorType.Memory:
                                    VectorDatabase.Instance.AddVector(req.MemoryId, vector);
                                    // [NEW] 複製給其他 ID
                                    if (req.CopyToIds?.Count > 0)
                                    {
                                        foreach (var copyId in req.CopyToIds)
                                            VectorDatabase.Instance.AddVector(copyId, vector);
                                    }
                                    break;
                                case VectorType.ContextDef:
                                case VectorType.ContextText:
                                    SemanticCache.Instance.AddToCache(req.Type, req.ContextKey, vector);
                                    break;
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(50, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error($"[RimTalk] LocalProcessor error: {ex.Message}");
                }
            }
        }

        // === DTO ===

        private class VectorRequest
        {
            public VectorType Type;
            public Guid MemoryId;
            public int ContextKey;
            public string Text;
            public List<Guid> CopyToIds;  // [NEW] 複製向量的目標 ID
        }
    }
}
