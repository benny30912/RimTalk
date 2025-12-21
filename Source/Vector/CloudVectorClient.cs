using RimTalk.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Verse;
using static UnityEngine.Networking.UnityWebRequest;

namespace RimTalk.Vector
{
    /// <summary>
    /// SiliconFlow bge-m3 雲端向量客戶端
    /// </summary>
    public class CloudVectorClient
    {
        private const string ENDPOINT = "https://api.siliconflow.cn/v1/embeddings";
        private const string MODEL = "BAAI/bge-m3";
        private const int VECTOR_DIM = 1024;  // bge-m3 維度

        // [NEW] 冷卻狀態（由外部 VectorQueueService 管理，這裡僅保留錯誤碼檢測）
        public bool IsRateLimited { get; private set; } = false;

        private static CloudVectorClient _instance;
        private static readonly object _lock = new();

        public static CloudVectorClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new CloudVectorClient();
                    }
                }
                return _instance;
            }
        }

        private CloudVectorClient() { }

        /// <summary>
        /// 單條文本嵌入（異步）
        /// </summary>
        public async Task<float[]> ComputeEmbeddingAsync(string text)
        {
            var results = await ComputeEmbeddingsBatchAsync(new List<string> { text });
            return results.FirstOrDefault() ?? new float[VECTOR_DIM];
        }

        /// <summary>
        /// 批次文本嵌入（異步）
        /// </summary>
        public async Task<List<float[]>> ComputeEmbeddingsBatchAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
                return new List<float[]>();

            string apiKey = Settings.Get().VectorApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log.Warning("[RimTalk] CloudVectorClient: No API key configured");
                return texts.Select(_ => new float[VECTOR_DIM]).ToList();
            }

            try
            {
                // 建構請求
                var requestBody = new EmbeddingRequest
                {
                    Model = MODEL,
                    Input = texts,
                    EncodingFormat = "float"
                };

                string jsonBody = JsonUtil.SerializeToJson(requestBody);

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

                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    // [NEW] 檢測限流錯誤
                    if (request.responseCode == 403 || request.responseCode == 429)
                    {
                        IsRateLimited = true;
                        Log.Warning($"[RimTalk] CloudVectorClient 限流: {request.responseCode}");
                    }
                    else
                    {
                        Log.Error($"[RimTalk] CloudVectorClient error: {request.error}");
                    }
                    return texts.Select(_ => new float[VECTOR_DIM]).ToList();
                }
                // 成功時重置限流狀態
                IsRateLimited = false;

                var response = JsonUtil.DeserializeFromJson<EmbeddingResponse>(request.downloadHandler.text);
                if (response?.Data == null)
                {
                    Log.Warning("[RimTalk] CloudVectorClient: Empty response");
                    return texts.Select(_ => new float[VECTOR_DIM]).ToList();
                }

                // 按 index 排序並提取向量
                return response.Data
                    .OrderBy(d => d.Index)
                    .Select(d => NormalizeVector(d.Embedding))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] CloudVectorClient exception: {ex.Message}");
                return texts.Select(_ => new float[VECTOR_DIM]).ToList();
            }
        }

        /// <summary>
        /// L2 正規化向量
        /// </summary>
        private float[] NormalizeVector(float[] vector)
        {
            if (vector == null || vector.Length == 0) return new float[VECTOR_DIM];

            double norm = 0;
            foreach (float v in vector) norm += v * v;
            norm = Math.Sqrt(norm);

            if (norm > 1e-12)
            {
                for (int i = 0; i < vector.Length; i++)
                    vector[i] = (float)(vector[i] / norm);
            }
            return vector;
        }

        // --- DTO ---
        [DataContract]
        private class EmbeddingRequest
        {
            [DataMember(Name = "model")] public string Model { get; set; }
            [DataMember(Name = "input")] public List<string> Input { get; set; }
            [DataMember(Name = "encoding_format")] public string EncodingFormat { get; set; }
        }

        [DataContract]
        private class EmbeddingResponse
        {
            [DataMember(Name = "data")] public List<EmbeddingData> Data { get; set; }
        }

        [DataContract]
        private class EmbeddingData
        {
            [DataMember(Name = "index")] public int Index { get; set; }
            [DataMember(Name = "embedding")] public float[] Embedding { get; set; }
        }
    }
}
