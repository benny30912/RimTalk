using RimTalk.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;
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
        /// [NEW] 批次取得向量（快取優先 + 批次計算未命中項目）
        /// 優勢：比逐個呼叫 GetVectorForDef/Text 快得多
        /// </summary>
        /// <param name="items">Context 項目清單</param>
        /// <returns>向量清單（順序對應）</returns>
        public List<float[]> GetVectorsBatch(List<ContextItem> items, bool isQuery = false)
        {
            if (items == null || items.Count == 0 || !VectorService.Instance.IsInitialized)
                return new List<float[]>();

            var results = new float[items.Count][];    // 保持與輸入相同順序
            var uncachedIndices = new List<int>();     // 未命中快取的索引
            var uncachedTexts = new List<string>();    // 未命中快取的文本
            var uncachedKeys = new List<int>();        // 未命中快取的 Key

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

                    // 查詢 Def 快取
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

                    // 查詢 Text 快取
                    if (_textCache.TryGetValue(key, out var cached))
                    {
                        results[i] = cached;
                        continue;
                    }
                }

                // 未命中，加入待計算清單
                uncachedIndices.Add(i);
                uncachedTexts.Add(text);
                uncachedKeys.Add(key);
            }

            // === 階段二：批次計算未命中項目 ===
            if (uncachedTexts.Count > 0)
            {
                var computed = VectorService.Instance.ComputeEmbeddingsBatch(uncachedTexts, isQuery);

                // === 階段三：填回結果並更新快取 ===
                for (int j = 0; j < uncachedIndices.Count && j < computed.Count; j++)
                {
                    int i = uncachedIndices[j];
                    int key = uncachedKeys[j];
                    results[i] = computed[j];

                    // 更新對應的快取
                    var item = items[i];
                    if (item.Type == ContextItem.ItemType.Def)
                        _defCache.TryAdd(key, computed[j]);
                    else
                        _textCache.TryAdd(key, computed[j]);
                }
            }

            // 過濾掉 null（可能因為空項目）
            return results.Where(v => v != null).ToList();
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
