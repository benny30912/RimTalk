using RimTalk.Source.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
namespace RimTalk.Vector
{
    /// <summary>
    /// 記憶向量二進制儲存服務
    /// </summary>
    public class VectorDatabase
    {
        private const int DB_VERSION = 1;
        private static VectorDatabase _instance;
        private static readonly object _instanceLock = new();
        // Key: MemoryRecord.Id (Guid)
        private readonly ConcurrentDictionary<Guid, float[]> _vectorStore = new();
        public static VectorDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new VectorDatabase();
                    }
                }
                return _instance;
            }
        }
        private VectorDatabase() { }
        // --- CRUD ---
        public void AddVector(Guid id, float[] vector)
        {
            if (vector == null) return;
            _vectorStore[id] = vector;
        }
        public void RemoveVector(Guid id)
        {
            _vectorStore.TryRemove(id, out _);
        }
        public float[] GetVector(Guid id)
        {
            return _vectorStore.TryGetValue(id, out var v) ? v : null;
        }
        // --- 二進制 I/O ---
        /// <summary>
        /// 保存向量庫到磁碟（僅保存有效向量，自動清理孤兒）
        /// </summary>
        public void SaveToDisk(string filePath, Dictionary<int, PawnMemoryData> pawnMemories)
        {
            try
            {
                // 確保資料夾存在
                var dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                // [NEW] 收集所有有效的記憶 ID
                var validIds = new HashSet<Guid>();
                if (pawnMemories != null)
                {
                    foreach (var data in pawnMemories.Values)
                    {
                        if (data == null) continue;
                        foreach (var mem in (data.ShortTermMemories ?? [])
                            .Concat(data.MediumTermMemories ?? [])
                            .Concat(data.LongTermMemories ?? []))
                        {
                            if (mem != null) validIds.Add(mem.Id);
                        }
                    }
                }
                // [NEW] 只儲存有效向量
                var validVectors = _vectorStore
                    .Where(kvp => validIds.Contains(kvp.Key))
                    .ToList();
                using var fs = new FileStream(filePath, FileMode.Create);
                using var writer = new BinaryWriter(fs);
                // 版本號
                writer.Write(DB_VERSION);
                // 數量
                writer.Write(validVectors.Count);
                foreach (var kvp in validVectors)
                {
                    // 寫入 Guid (16 bytes)
                    writer.Write(kvp.Key.ToByteArray());
                    // 寫入向量
                    writer.Write(kvp.Value.Length);
                    foreach (float v in kvp.Value)
                        writer.Write(v);
                }
                Log.Message($"[RimTalk] VectorDatabase saved: {validVectors.Count} vectors (filtered from {_vectorStore.Count})");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] VectorDatabase save failed: {ex.Message}");
            }
        }
        /// <summary>
        /// 從磁碟載入向量庫
        /// </summary>
        public bool LoadFromDisk(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open);
                using var reader = new BinaryReader(fs);
                // 版本檢查
                int version = reader.ReadInt32();
                if (version != DB_VERSION)
                {
                    Log.Warning($"[RimTalk] VectorDatabase version mismatch ({version} vs {DB_VERSION}), skipping...");
                    return false;
                }
                // 數量
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    // 讀取 Guid (16 bytes)
                    byte[] guidBytes = reader.ReadBytes(16);
                    var id = new Guid(guidBytes);
                    // 讀取向量
                    int length = reader.ReadInt32();
                    float[] vector = new float[length];
                    for (int j = 0; j < length; j++)
                        vector[j] = reader.ReadSingle();
                    _vectorStore[id] = vector;
                }
                Log.Message($"[RimTalk] VectorDatabase loaded: {count} vectors");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk] VectorDatabase load failed: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 清空向量庫
        /// </summary>
        public void Clear() => _vectorStore.Clear();
        public int Count => _vectorStore.Count;
    }
}
