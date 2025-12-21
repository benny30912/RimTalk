using LudeonTK;  // RimWorld Debug 框架
using RimTalk.Data;
using RimTalk.Source.Memory;
using RimTalk.Vector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Tests
{
    /// <summary>
    /// VectorDatabase 測試集
    /// 這些方法會出現在開發者模式的 Debug Actions 選單中
    /// </summary>
    public static class VectorDatabaseTests
    {
        // ---------- 功能測試 ----------

        /// <summary>
        /// CRUD 完整測試
        /// 路徑：Debug Actions > RimTalk > Test_CRUD
        /// </summary>
        [DebugAction("RimTalk", "Test_CRUD", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_CRUD()
        {
            Log.Message("[VectorDBTest] ========== 開始 CRUD 測試 ==========");

            var db = VectorDatabase.Instance;
            int initialCount = db.Count;

            // Test 1: AddVector
            var testId = Guid.NewGuid();
            float[] testVector = new float[768];
            for (int i = 0; i < 768; i++) testVector[i] = (float)i / 768f;

            db.AddVector(testId, testVector);
            bool addSuccess = db.Count == initialCount + 1;
            Log.Message($"[VectorDBTest] Add: {(addSuccess ? "✅ PASS" : "❌ FAIL")} - Count: {db.Count}");

            // Test 2: GetVector
            float[] retrieved = db.GetVector(testId);
            bool getSuccess = retrieved != null && retrieved.Length == 768;
            Log.Message($"[VectorDBTest] Get: {(getSuccess ? "✅ PASS" : "❌ FAIL")}");

            // Test 3: GetVector (不存在)
            float[] notFound = db.GetVector(Guid.NewGuid());
            bool notFoundSuccess = notFound == null;
            Log.Message($"[VectorDBTest] GetNotFound: {(notFoundSuccess ? "✅ PASS" : "❌ FAIL")}");

            // Test 4: RemoveVector
            db.RemoveVector(testId);
            bool removeSuccess = db.GetVector(testId) == null && db.Count == initialCount;
            Log.Message($"[VectorDBTest] Remove: {(removeSuccess ? "✅ PASS" : "❌ FAIL")}");

            // Test 5: AddVector with null
            db.AddVector(Guid.NewGuid(), null);
            Log.Message($"[VectorDBTest] AddNull: ✅ PASS (no exception)");

            Log.Message("[VectorDBTest] ========== CRUD 測試完成 ==========");
        }

        /// <summary>
        /// 顯示當前向量庫狀態
        /// </summary>
        [DebugAction("RimTalk", "Show_VectorDB_Status", allowedGameStates = AllowedGameStates.Playing)]
        public static void Show_VectorDB_Status()
        {
            var db = VectorDatabase.Instance;
            Log.Message($"[VectorDB] 當前向量數量: {db.Count}");
        }

        /// <summary>
        /// 清空向量庫
        /// </summary>
        [DebugAction("RimTalk", "Clear_VectorDB", allowedGameStates = AllowedGameStates.Playing)]
        public static void Clear_VectorDB()
        {
            int before = VectorDatabase.Instance.Count;
            VectorDatabase.Instance.Clear();
            Log.Message($"[VectorDB] 已清空向量庫: {before} → 0");
        }

        // ---------- 效能測試 ----------

        /// <summary>
        /// 向量計算效能測試（支援雲端/本地雙軌）
        /// 注意：雲端 API 為異步執行，結果會稍後顯示在 Log 中
        /// </summary>
        [DebugAction("RimTalk", "Test_VectorPerformance", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_VectorPerformance()
        {
            bool isCloud = Settings.Get().UseCloudVectorService;
            string mode = isCloud ? "雲端 API" : "本地 ONNX";
            Log.Message($"[PerfTest] ========== 開始向量計算效能測試 ({mode}) ==========");

            string testText = "這是一段用於測試向量計算效能的中文文本，包含日常對話內容。";

            if (isCloud)
            {
                // 雲端模式：使用 Task.Run 異步執行，避免主執行緒阻塞
                Task.Run(async () =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();

                        const int iterations = 3;  // 雲端測試減少次數（API 限流）
                        for (int i = 0; i < iterations; i++)
                        {
                            float[] vector = await CloudVectorClient.Instance.ComputeEmbeddingAsync(testText);
                        }

                        sw.Stop();
                        double avgMs = sw.ElapsedMilliseconds / (double)iterations;

                        // 雲端目標較寬鬆：<2000ms（網路延遲）
                        bool passed = avgMs < 2000;
                        Log.Message($"[PerfTest] 雲端單條向量計算: {avgMs:F2} ms (目標 <2000ms) {(passed ? "✅ PASS" : "❌ FAIL")}");
                        Log.Message($"[PerfTest] 向量維度: 1024 (bge-m3)");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[PerfTest] 雲端測試失敗: {ex.Message}");
                    }
                    Log.Message("[PerfTest] ========== 雲端效能測試完成 ==========");
                });

                Log.Message("[PerfTest] 雲端測試已啟動（異步），結果稍後顯示...");
            }
            else
            {
                // 本地模式：同步執行
                var sw = System.Diagnostics.Stopwatch.StartNew();

                const int iterations = 10;
                for (int i = 0; i < iterations; i++)
                {
                    float[] vector = VectorService.Instance.LocalComputeEmbedding(testText, false);
                }

                sw.Stop();
                double avgMs = sw.ElapsedMilliseconds / (double)iterations;

                bool passed = avgMs < 100;
                Log.Message($"[PerfTest] 本地單條向量計算: {avgMs:F2} ms (目標 <100ms) {(passed ? "✅ PASS" : "❌ FAIL")}");
                Log.Message($"[PerfTest] 向量維度: 768 (text2vec-base-chinese)");
                Log.Message("[PerfTest] ========== 本地效能測試完成 ==========");
            }
        }

        /// <summary>
        /// 顯示當前向量服務模式
        /// </summary>
        [DebugAction("RimTalk", "Show_VectorMode", allowedGameStates = AllowedGameStates.Playing)]
        public static void Show_VectorMode()
        {
            bool isCloud = Settings.Get().UseCloudVectorService;
            string mode = isCloud ? "雲端 API (bge-m3, 1024維)" : "本地 ONNX (text2vec, 768維)";
            Log.Message($"[VectorService] 當前模式: {mode}");
        }

        /// <summary>
        /// 記憶檢索效能測試
        /// </summary>
        [DebugAction("RimTalk", "Test_RetrievalPerformance", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_RetrievalPerformance()
        {
            Log.Message("[PerfTest] 開始記憶檢索效能測試...");

            var db = VectorDatabase.Instance;
            var testIds = new System.Collections.Generic.List<Guid>();

            // 準備 100 條測試向量
            for (int i = 0; i < 100; i++)
            {
                var id = Guid.NewGuid();
                var vec = new float[768];
                for (int j = 0; j < 768; j++) vec[j] = (float)new Random(i * 768 + j).NextDouble();
                db.AddVector(id, vec);
                testIds.Add(id);
            }

            // 測試檢索時間
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var id in testIds)
            {
                float[] _ = db.GetVector(id);
            }
            sw.Stop();

            bool passed = sw.ElapsedMilliseconds < 50;
            Log.Message($"[PerfTest] 100 條記憶檢索: {sw.ElapsedMilliseconds} ms (目標 <50ms) {(passed ? "✅ PASS" : "❌ FAIL")}");

            // 清理
            foreach (var id in testIds) db.RemoveVector(id);
        }

        // ---------- 持久化測試 ----------

        /// <summary>
        /// 檢查持久化檔案狀態
        /// </summary>
        [DebugAction("RimTalk", "Check_PersistenceFiles", allowedGameStates = AllowedGameStates.Playing)]
        public static void Check_PersistenceFiles()
        {
            string configPath = GenFilePaths.ConfigFolderPath;
            string rimTalkDir = System.IO.Path.Combine(configPath, "RimTalk");

            Log.Message($"[Persistence] 儲存路徑: {rimTalkDir}");
            Log.Message($"[Persistence] 目錄存在: {System.IO.Directory.Exists(rimTalkDir)}");

            if (System.IO.Directory.Exists(rimTalkDir))
            {
                var files = System.IO.Directory.GetFiles(rimTalkDir, "*.bin");
                foreach (var f in files)
                {
                    var info = new System.IO.FileInfo(f);
                    Log.Message($"[Persistence] 檔案: {info.Name}, 大小: {info.Length} bytes");
                }
            }
        }

        // === 請在 VectorDatabaseTests.cs 中新增以下測試方法 ===

        // ---------- 向量同步刪除測試 ----------

        /// <summary>
        /// 測試刪除記憶時向量是否同步刪除
        /// 執行前：確保有至少一條記憶
        /// </summary>
        [DebugAction("RimTalk", "Test_VectorSyncDeletion", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_VectorSyncDeletion()
        {
            Log.Message("[SyncTest] ========== 開始向量同步刪除測試 ==========");

            var db = VectorDatabase.Instance;
            int beforeCount = db.Count;
            Log.Message($"[SyncTest] 刪除前向量數量: {beforeCount}");

            // 找到一個有記憶的 Pawn
            var worldComp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (worldComp?.PawnMemories == null || !worldComp.PawnMemories.Any())
            {
                Log.Warning("[SyncTest] 找不到任何記憶，請先進行一些對話");
                return;
            }

            // 取得第一個有記憶的 Pawn
            var firstPawnData = worldComp.PawnMemories.Values.FirstOrDefault(d =>
                d?.ShortTermMemories?.Any() == true ||
                d?.MediumTermMemories?.Any() == true ||
                d?.LongTermMemories?.Any() == true);

            if (firstPawnData == null)
            {
                Log.Warning("[SyncTest] 找不到任何記憶資料");
                return;
            }

            // 找一條記錄來刪除
            MemoryRecord targetMemory = null;
            targetMemory = firstPawnData.ShortTermMemories?.FirstOrDefault()
                           ?? firstPawnData.MediumTermMemories?.FirstOrDefault()
                           ?? firstPawnData.LongTermMemories?.FirstOrDefault();

            if (targetMemory == null)
            {
                Log.Warning("[SyncTest] 找不到可刪除的記憶");
                return;
            }

            // 檢查這條記憶是否有對應向量
            bool hasVectorBefore = db.GetVector(targetMemory.Id) != null;
            Log.Message($"[SyncTest] 目標記憶 ID: {targetMemory.Id.ToString().Substring(0, 8)}");
            Log.Message($"[SyncTest] 刪除前有對應向量: {hasVectorBefore}");

            // 執行刪除
            MemoryService.DeleteMemory(firstPawnData.Pawn, targetMemory);

            // 檢查結果
            int afterCount = db.Count;
            bool hasVectorAfter = db.GetVector(targetMemory.Id) != null;

            Log.Message($"[SyncTest] 刪除後向量數量: {afterCount}");
            Log.Message($"[SyncTest] 刪除後有對應向量: {hasVectorAfter}");

            bool passed = !hasVectorAfter && (hasVectorBefore ? afterCount == beforeCount - 1 : afterCount == beforeCount);
            Log.Message($"[SyncTest] 同步刪除測試: {(passed ? "✅ PASS" : "❌ FAIL")}");

            Log.Message("[SyncTest] ========== 向量同步刪除測試完成 ==========");
        }

        // ---------- 邊界情況測試 ----------

        /// <summary>
        /// 空資料庫測試
        /// </summary>
        [DebugAction("RimTalk", "Test_EmptyDatabase", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_EmptyDatabase()
        {
            Log.Message("[EdgeTest] ========== 開始空資料庫測試 ==========");

            var db = VectorDatabase.Instance;
            int originalCount = db.Count;

            // 暫時清空
            db.Clear();

            // 測試空庫操作
            bool clearOk = db.Count == 0;
            bool getNull = db.GetVector(Guid.NewGuid()) == null;

            // 嘗試移除不存在的向量（不應報錯）
            bool removeNoError = true;
            try
            {
                db.RemoveVector(Guid.NewGuid());
            }
            catch
            {
                removeNoError = false;
            }

            Log.Message($"[EdgeTest] 空庫 Count==0: {(clearOk ? "✅" : "❌")}");
            Log.Message($"[EdgeTest] 空庫 Get==null: {(getNull ? "✅" : "❌")}");
            Log.Message($"[EdgeTest] 刪除不存在向量無報錯: {(removeNoError ? "✅" : "❌")}");

            // 測試空庫存檔
            bool saveNoError = true;
            try
            {
                string testPath = System.IO.Path.Combine(
                    GenFilePaths.ConfigFolderPath,
                    "RimTalk",
                    "test_empty.bin"
                );
                db.SaveToDisk(testPath, new Dictionary<int, PawnMemoryData>());
                // 清理測試檔案
                if (System.IO.File.Exists(testPath))
                    System.IO.File.Delete(testPath);
            }
            catch (Exception ex)
            {
                Log.Error($"[EdgeTest] 空庫存檔報錯: {ex.Message}");
                saveNoError = false;
            }
            Log.Message($"[EdgeTest] 空庫存檔無報錯: {(saveNoError ? "✅" : "❌")}");

            Log.Message($"[EdgeTest] 原始向量數量: {originalCount}（測試不會還原，請重新載入存檔）");
            Log.Message("[EdgeTest] ========== 空資料庫測試完成 ==========");
        }

        /// <summary>
        /// 重複 ID 覆蓋測試
        /// </summary>
        [DebugAction("RimTalk", "Test_OverwriteVector", allowedGameStates = AllowedGameStates.Playing)]
        public static void Test_OverwriteVector()
        {
            Log.Message("[EdgeTest] ========== 開始重複 ID 覆蓋測試 ==========");

            var db = VectorDatabase.Instance;
            var testId = Guid.NewGuid();

            float[] v1 = new float[] { 1f, 2f, 3f };
            float[] v2 = new float[] { 4f, 5f, 6f };

            db.AddVector(testId, v1);
            float[] afterFirst = db.GetVector(testId);

            db.AddVector(testId, v2);
            float[] afterSecond = db.GetVector(testId);

            bool overwriteOk = afterSecond != null &&
                               Math.Abs(afterSecond[0] - 4f) < 0.001f;

            Log.Message($"[EdgeTest] 第一次寫入值: {afterFirst?[0]}");
            Log.Message($"[EdgeTest] 覆蓋後值: {afterSecond?[0]}");
            Log.Message($"[EdgeTest] 重複 ID 覆蓋: {(overwriteOk ? "✅ PASS" : "❌ FAIL")}");

            // 清理
            db.RemoveVector(testId);

            Log.Message("[EdgeTest] ========== 重複 ID 覆蓋測試完成 ==========");
        }
    }
}
