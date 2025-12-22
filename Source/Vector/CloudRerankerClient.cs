using RimTalk.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// SiliconFlow bge-reranker-v2-m3 雲端 Reranker 客戶端
    /// 職責：對候選記憶進行精確語意重排序
    /// </summary>
    public class CloudRerankerClient
    {
        // API 端點與模型設定 (硬編碼)
        private const string ENDPOINT = "https://api.siliconflow.cn/v1/rerank";
        private const string MODEL = "BAAI/bge-reranker-v2-m3";

        // 限流狀態（由外部邏輯判斷是否降級）
        public bool IsRateLimited { get; private set; } = false;

        // Singleton 模式
        private static CloudRerankerClient _instance;
        private static readonly object _lock = new();

        public static CloudRerankerClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new CloudRerankerClient();
                    }
                }
                return _instance;
            }
        }

        private CloudRerankerClient() { }

        /// <summary>
        /// 對候選文檔進行 Rerank 排序（異步）
        /// </summary>
        /// <param name="query">查詢文本（Context 組合）</param>
        /// <param name="documents">候選文檔列表（記憶摘要）</param>
        /// <param name="topN">返回前 N 個結果</param>
        /// <returns>按相關性排序的結果列表 (index, relevance_score)</returns>
        public async Task<List<RerankResult>> RerankAsync(string query, List<string> documents, int topN = 20)
        {
            // [安全檢查] 空輸入直接返回
            if (string.IsNullOrWhiteSpace(query) || documents == null || documents.Count == 0)
                return new List<RerankResult>();

            // [安全檢查] API Key 必須存在
            string apiKey = Settings.Get().VectorApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log.Warning("[RimTalk] CloudRerankerClient: 未設定 API Key，跳過 Rerank");
                return new List<RerankResult>();
            }

            try
            {
                // 建構請求 Body
                var requestBody = new RerankRequest
                {
                    Model = MODEL,
                    Query = query,
                    Documents = documents,
                    TopN = Math.Min(topN, documents.Count), // 不超過文檔數量
                    ReturnDocuments = false // 節省傳輸：不返回原文
                };

                string jsonBody = JsonUtil.SerializeToJson(requestBody);

                // 發送 HTTP 請求
                using var request = new UnityWebRequest(ENDPOINT, "POST");
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Delay(50);
                }

                // 錯誤處理
                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    // 檢測限流錯誤 (403/429)
                    if (request.responseCode == 403 || request.responseCode == 429)
                    {
                        IsRateLimited = true;
                        Log.Warning($"[RimTalk] CloudRerankerClient 限流: {request.responseCode}");
                    }
                    else
                    {
                        Log.Error($"[RimTalk] CloudRerankerClient 錯誤: {request.error}");
                    }
                    return new List<RerankResult>();
                }

                // 成功時重置限流狀態
                IsRateLimited = false;

                // 解析回應
                var response = JsonUtil.DeserializeFromJson<RerankResponse>(request.downloadHandler.text);
                if (response?.Results == null)
                {
                    Log.Warning("[RimTalk] CloudRerankerClient: 回應為空");
                    return new List<RerankResult>();
                }

                // 按 relevance_score 降序排序並返回
                return response.Results
                    .OrderByDescending(r => r.RelevanceScore)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] CloudRerankerClient 例外: {ex.Message}");
                return new List<RerankResult>();
            }
        }

        // ========================
        // DTO 類別定義
        // ========================

        [DataContract]
        private class RerankRequest
        {
            [DataMember(Name = "model")]
            public string Model { get; set; }

            [DataMember(Name = "query")]
            public string Query { get; set; }

            [DataMember(Name = "documents")]
            public List<string> Documents { get; set; }

            [DataMember(Name = "top_n")]
            public int TopN { get; set; }

            [DataMember(Name = "return_documents")]
            public bool ReturnDocuments { get; set; }
        }

        [DataContract]
        private class RerankResponse
        {
            [DataMember(Name = "results")]
            public List<RerankResult> Results { get; set; }
        }

        /// <summary>
        /// Rerank 結果項目（公開供外部使用）
        /// </summary>
        [DataContract]
        public class RerankResult
        {
            [DataMember(Name = "index")]
            public int Index { get; set; }

            [DataMember(Name = "relevance_score")]
            public float RelevanceScore { get; set; }
        }
    }
}
