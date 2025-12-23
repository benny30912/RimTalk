using RimTalk.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// Def/描述 語意向量快取（含持久化）
    /// </summary>
    public class SemanticCache
    {
        // 快取版本號，模型更新時需遞增
        private const int CACHE_VERSION = 1;

        private static SemanticCache _instance;
        private static readonly object _instanceLock = new object();

        // Def 向量快取 (Key: Def.shortHash)
        private readonly ConcurrentDictionary<int, float[]> _defCache
            = new ConcurrentDictionary<int, float[]>();

        // 固定描述向量快取 (Key: 描述文本 HashCode)
        private readonly ConcurrentDictionary<int, float[]> _textCache
            = new ConcurrentDictionary<int, float[]>();

        public static SemanticCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null) _instance = new SemanticCache();
                    }
                }
                return _instance;
            }
        }

        private SemanticCache() { }

        /// <summary>
        /// [MODIFY] 批次取得向量（快取優先 + 異步計算未命中項目）
        /// 雲端模式：async 計算，失敗時加入佇列
        /// 本地模式：同步計算
        /// </summary>
        public async Task<List<float[]>> GetVectorsBatchAsync(List<ContextItem> items, bool isQuery = false)
        {
            if (items == null || items.Count == 0)
                return new List<float[]>();
            var results = new float[items.Count][];
            var uncachedIndices = new List<int>();
            var uncachedTexts = new List<string>();
            var uncachedKeys = new List<int>();
            // === 階段一：查詢快取 ===
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int key;
                string text;
                if (item.Type == ContextItem.ItemType.Def)
                {
                    if (item.Def == null) continue;
                    key = item.Def.shortHash;
                    text = SemanticMapper.GetSemanticTextFromDef(item.Def);
                    if (_defCache.TryGetValue(key, out var cached))
                    {
                        results[i] = cached;
                        continue;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(item.Text)) continue;
                    text = item.Text;
                    key = text.GetHashCode();
                    if (_textCache.TryGetValue(key, out var cached))
                    {
                        results[i] = cached;
                        continue;
                    }
                }
                uncachedIndices.Add(i);
                uncachedTexts.Add(text);
                uncachedKeys.Add(key);
            }
            // === 階段二：計算未命中項目 ===
            if (uncachedTexts.Count > 0)
            {
                List<float[]> computed;
                if (Settings.Get().UseCloudVectorService)
                {
                    // 雲端模式：異步計算，失敗時加入佇列
                    try
                    {
                        computed = await CloudVectorClient.Instance.ComputeEmbeddingsBatchAsync(uncachedTexts);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimTalk] Context 向量計算失敗，加入佇列: {ex.Message}");
                        for (int j = 0; j < uncachedTexts.Count; j++)
                        {
                            int idx = uncachedIndices[j];
                            var item = items[idx];
                            int key = uncachedKeys[j];
                            VectorType type = item.Type == ContextItem.ItemType.Def
                                ? VectorType.ContextDef
                                : VectorType.ContextText;
                            VectorQueueService.Instance.Enqueue(type, key, uncachedTexts[j]);
                        }
                        computed = new List<float[]>();
                    }
                }
                else
                {
                    // 本地模式：在背景執行緒計算
                    computed = await Task.Run(() =>
                        VectorService.Instance.LocalComputeEmbeddingsBatch(uncachedTexts, isQuery)
                    );
                }
                // === 階段三：填回結果並更新快取 ===
                for (int j = 0; j < uncachedIndices.Count && j < computed.Count; j++)
                {
                    int i = uncachedIndices[j];
                    int key = uncachedKeys[j];
                    results[i] = computed[j];
                    var item = items[i];
                    if (item.Type == ContextItem.ItemType.Def)
                        _defCache.TryAdd(key, computed[j]);
                    else
                        _textCache.TryAdd(key, computed[j]);
                }
            }
            // [MOD] 不過濾 null，保持與 items 索引對應
            // 呼叫方需自行處理 null 向量（如 CalculateMaxSim 已有 null 檢查）
            return results.ToList();
        }

        /// <summary>
        /// 檢查是否已有快取向量
        /// </summary>
        public bool HasCachedVector(VectorType type, int key)
        {
            return type == VectorType.ContextDef
                ? _defCache.ContainsKey(key)
                : _textCache.ContainsKey(key);
        }
        /// <summary>
        /// 外部寫入快取（供佇列處理器使用）
        /// </summary>
        public void AddToCache(VectorType type, int key, float[] vector)
        {
            if (type == VectorType.ContextDef)
                _defCache.TryAdd(key, vector);
            else
                _textCache.TryAdd(key, vector);
        }

        /// <summary>
        /// 保存快取到二進制檔案
        /// </summary>
        public void SaveToDisk(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Create))
                using (var writer = new BinaryWriter(fs))
                {
                    // 寫入版本號
                    writer.Write(CACHE_VERSION);

                    // 寫入 Def 快取
                    writer.Write(_defCache.Count);
                    foreach (var kvp in _defCache)
                    {
                        writer.Write(kvp.Key);
                        WriteVector(writer, kvp.Value);
                    }

                    // 寫入 Text 快取
                    writer.Write(_textCache.Count);
                    foreach (var kvp in _textCache)
                    {
                        writer.Write(kvp.Key);
                        WriteVector(writer, kvp.Value);
                    }
                }
                Log.Message($"[RimTalk] SemanticCache saved: {_defCache.Count} defs, {_textCache.Count} texts");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] SemanticCache save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 從二進制檔案載入快取
        /// </summary>
        public bool LoadFromDisk(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open))
                using (var reader = new BinaryReader(fs))
                {
                    // 檢查版本號
                    int version = reader.ReadInt32();
                    if (version != CACHE_VERSION)
                    {
                        Log.Warning($"[RimTalk] SemanticCache version mismatch ({version} vs {CACHE_VERSION}), rebuilding...");
                        return false;
                    }

                    // 讀取 Def 快取
                    int defCount = reader.ReadInt32();
                    for (int i = 0; i < defCount; i++)
                    {
                        int key = reader.ReadInt32();
                        float[] vector = ReadVector(reader);
                        _defCache.TryAdd(key, vector);
                    }

                    // 讀取 Text 快取
                    int textCount = reader.ReadInt32();
                    for (int i = 0; i < textCount; i++)
                    {
                        int key = reader.ReadInt32();
                        float[] vector = ReadVector(reader);
                        _textCache.TryAdd(key, vector);
                    }

                    Log.Message($"[RimTalk] SemanticCache loaded: {defCount} defs, {textCount} texts");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk] SemanticCache load failed: {ex.Message}");
                return false;
            }
        }

        private static void WriteVector(BinaryWriter writer, float[] vector)
        {
            writer.Write(vector.Length);
            foreach (float v in vector)
                writer.Write(v);
        }

        private static float[] ReadVector(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            float[] vector = new float[length];
            for (int i = 0; i < length; i++)
                vector[i] = reader.ReadSingle();
            return vector;
        }

        /// <summary>
        /// 清空所有快取
        /// </summary>
        public void Clear()
        {
            _defCache.Clear();
            _textCache.Clear();
            Log.Message("[RimTalk] SemanticCache cleared");
        }
    }
}
