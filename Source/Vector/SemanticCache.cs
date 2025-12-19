using System;
using System.Collections.Concurrent;
using System.IO;
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
        /// 取得 Def 的向量（快取優先）
        /// </summary>
        public float[] GetVectorForDef(Def def)
        {
            if (def == null || !VectorService.Instance.IsInitialized)
                return null;

            int key = def.shortHash;

            if (_defCache.TryGetValue(key, out float[] cached))
                return cached;

            string text = GetSemanticTextFromDef(def);
            float[] vector = VectorService.Instance.ComputeEmbedding(text);
            _defCache.TryAdd(key, vector);

            return vector;
        }

        /// <summary>
        /// 取得固定描述文本的向量（快取優先）
        /// </summary>
        public float[] GetVectorForText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !VectorService.Instance.IsInitialized)
                return null;

            int key = text.GetHashCode();

            if (_textCache.TryGetValue(key, out float[] cached))
                return cached;

            float[] vector = VectorService.Instance.ComputeEmbedding(text);
            _textCache.TryAdd(key, vector);

            return vector;
        }

        private static string GetSemanticTextFromDef(Def def)
        {
            if (def == null) return string.Empty;
            string label = def.label ?? def.defName;
            string desc = def.description ?? "";
            return string.IsNullOrWhiteSpace(desc) ? label : $"{label}: {desc}";
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
        /// 清除快取
        /// </summary>
        public void Clear()
        {
            _defCache.Clear();
            _textCache.Clear();
        }

        public (int defCount, int textCount) GetStats()
            => (_defCache.Count, _textCache.Count);
    }
}
