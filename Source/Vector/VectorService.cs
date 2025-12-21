using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;  // [新增] 官方 Tokenizer 庫
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// 向量運算核心服務 (Singleton)
    /// 職責：僅負責 ONNX 模型加載、推論 (Inference) 與 數學計算。
    /// 不包含任何具體的記憶儲存或檢索邏輯。
    /// </summary>
    public class VectorService
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        private static VectorService _instance;
        private static readonly object _instanceLock = new object();
        private static readonly object _inferenceLock = new object(); // 保護模型推論的線程安全

        private InferenceSession _session;
        private bool _isInitialized = false;
        private static bool _nativeLoaded = false;

        // [修改] 使用官方 BertTokenizer 取代自定義詞表與方法
        private BertTokenizer _tokenizer;

        public bool IsInitialized => _isInitialized;

        public static VectorService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null) _instance = new VectorService();
                    }
                }
                return _instance;
            }
        }

        private VectorService() { }

        /// <summary>
        /// 初始化服務：加載模型與詞表
        /// </summary>
        public void Initialize(string modelPath, string vocabPath)
        {
            if (_isInitialized) return;

            try
            {
                Log.Message("[RimTalk] VectorService: Initializing...");

                // 預先載入原生 DLL（保持不變）
                // [修改] 跨平台原生庫預載入
                if (!_nativeLoaded)
                {
                    _nativeLoaded = true;  // 無論成功與否，只嘗試一次
                                           // 非 Windows 平台跳過預載入，讓 ONNX Runtime 自行處理
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Log.Message("[RimTalk] Non-Windows platform detected, skipping native DLL preload");
                    }
                    else
                    {
                        // Windows 專用：手動預載入 onnxruntime.dll
                        try
                        {
                            string nativeDllPath = Path.Combine(
                                Path.GetDirectoryName(modelPath),
                                "..",
                                "Native",
                                "onnxruntime.dll"
                            );
                            nativeDllPath = Path.GetFullPath(nativeDllPath);
                            if (File.Exists(nativeDllPath))
                            {
                                var handle = LoadLibrary(nativeDllPath);
                                Log.Message(handle != IntPtr.Zero
                                    ? $"[RimTalk] Preloaded native DLL: {nativeDllPath}"
                                    : $"[RimTalk] Failed to preload native DLL: {nativeDllPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimTalk] Native DLL preload error: {ex.Message}");
                        }
                    }
                }

                // 驗證模型檔案存在
                if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                {
                    Log.Warning($"[RimTalk] VectorService: Model not found at: {modelPath}");
                    return;
                }

                // [新增] 載入 BertTokenizer
                if (!File.Exists(vocabPath))
                {
                    Log.Warning($"[RimTalk] VectorService: Vocab not found at: {vocabPath}");
                    return;
                }

                // 使用 BertOptions 設定 lowercase（與原實現一致）
                var options = new BertOptions { LowerCaseBeforeTokenization = true };
                _tokenizer = BertTokenizer.Create(vocabPath, options);
                Log.Message($"[RimTalk] BertTokenizer loaded from: {vocabPath}");

                // ONNX Session 設定（保持不變）
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                sessionOptions.InterOpNumThreads = 2;
                sessionOptions.IntraOpNumThreads = 2;

                Log.Message($"[RimTalk] Loading model from: {modelPath}");
                _session = new InferenceSession(modelPath, sessionOptions);

                _isInitialized = true;

                // 預熱 (Warmup)
                Task.Run(() => ComputeEmbedding("Warmup initialization text."));

                Log.Message("[RimTalk] VectorService: Initialization complete!");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] VectorService Init Failed: {ex}");
                _isInitialized = false;
            }
        }

        // BGE 模型在進行「查詢 (Query)」的向量化時，通常需要加上特定的前綴指令，而在將「文件/記憶 (Document)」寫入資料庫時則不需要。
        // 這裡的「查詢」與「文件」，指的是如果我們根據上下文尋找語意最相關的記憶，那麼上下文向量是「查詢」需要特定前綴，而記憶向量是「文件」不需要。
        // BGE 模型的查詢前綴指令
        private const string BGE_QUERY_PREFIX = "为这个句子生成表示以用于检索相关文章：";

        /// <summary>
        /// 計算嵌入向量（自動選擇雲端或本地）
        /// </summary>
        public float[] ComputeEmbedding(string text, bool isQuery = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return GetEmptyVector();

            // 根據設定選擇服務
            if (Settings.Get().UseCloudVectorService)
            {
                return CloudVectorClient.Instance.ComputeEmbedding(text);
            }
            else
            {
                return LocalComputeEmbedding(text, isQuery);
            }
        }

        /// <summary>
        /// 批次計算嵌入向量（自動選擇雲端或本地）
        /// </summary>
        public List<float[]> ComputeEmbeddingsBatch(List<string> texts, bool isQuery = false)
        {
            if (texts == null || texts.Count == 0) return new List<float[]>();

            if (Settings.Get().UseCloudVectorService)
            {
                return CloudVectorClient.Instance.ComputeEmbeddingsBatch(texts);
            }
            else
            {
                return LocalComputeEmbeddingsBatch(texts, isQuery);
            }
        }

        /// <summary>
        /// 取得空向量（維度根據模式不同）
        /// </summary>
        public float[] GetEmptyVector()
        {
            int dim = Settings.Get().UseCloudVectorService ? 1024 : 768;
            return new float[dim];
        }

        // 將原本的 ComputeEmbedding 改名為 LocalComputeEmbedding
        private float[] LocalComputeEmbedding(string text, bool isQuery)
        {
            if (!_isInitialized) return new float[768];
            string input = isQuery ? BGE_QUERY_PREFIX + text : text;
            lock (_inferenceLock)
            {
                return InternalInference(new List<string> { input }).First();
            }
        }

        private List<float[]> LocalComputeEmbeddingsBatch(List<string> texts, bool isQuery)
        {
            if (!_isInitialized) return texts.Select(_ => new float[768]).ToList();
            var inputs = isQuery ? texts.Select(t => BGE_QUERY_PREFIX + t).ToList() : texts;
            lock (_inferenceLock)
            {
                return InternalInference(inputs);
            }
        }

        /// <summary>
        /// 計算餘弦相似度 (Cosine Similarity)
        /// 假設輸入的向量都已經過 Normalize (長度為1)，則 DotProduct 等於 CosineSimilarity
        /// </summary>
        public static float CosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1.Length != vec2.Length) return 0f;

            float dot = 0f;
            for (int i = 0; i < vec1.Length; i++)
            {
                dot += vec1[i] * vec2[i];
            }
            return dot;
        }

        /// <summary>
        /// 核心推論邏輯 (支援 Batch)
        /// </summary>
        private List<float[]> InternalInference(List<string> texts)
        {
            try
            {
                int batchSize = texts.Count;
                int maxLen = 128;

                var inputIdsTensor = new DenseTensor<long>(new[] { batchSize, maxLen });
                var attentionMaskTensor = new DenseTensor<long>(new[] { batchSize, maxLen });
                var tokenTypeIdsTensor = new DenseTensor<long>(new[] { batchSize, maxLen });

                long[][] batchInputIds = new long[batchSize][];

                for (int b = 0; b < batchSize; b++)
                {
                    // [修改] 使用 BertTokenizer.EncodeToIds() 取代自定義 Tokenize()
                    // [修正] 正確的參數名稱和呼叫方式
                    var encodedIds = _tokenizer.EncodeToIds(
                        texts[b],
                        maxTokenCount: maxLen,  // 修正：maxTokenCount 而非 maxLength
                        out _,                   // normalizedText
                        out _                    // charsConsumed
                    );

                    int[] tokens = encodedIds.ToArray();
                    batchInputIds[b] = new long[tokens.Length];

                    for (int i = 0; i < maxLen; i++)
                    {
                        if (i < tokens.Length)
                        {
                            inputIdsTensor[b, i] = tokens[i];
                            attentionMaskTensor[b, i] = 1;  // [修改] 有 token 就是 1
                            batchInputIds[b][i] = tokens[i];
                        }
                        else
                        {
                            inputIdsTensor[b, i] = 0;  // PAD
                            attentionMaskTensor[b, i] = 0;
                        }
                        tokenTypeIdsTensor[b, i] = 0;
                    }
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
                };

                using (var results = _session.Run(inputs))
                {
                    var outputData = results.First().AsEnumerable<float>().ToArray();

                    var embeddings = new List<float[]>();
                    for (int b = 0; b < batchSize; b++)
                    {
                        embeddings.Add(MeanPooling(outputData, batchInputIds[b], b, maxLen));
                    }

                    return embeddings;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] Inference Error: {ex.Message}");
                return Enumerable.Repeat(new float[768], texts.Count).ToList();
            }
        }

        /// <summary>
        /// 平均池化：將 Sequence 的向量壓縮成一個 Sentence 向量
        /// </summary>
        private float[] MeanPooling(float[] flatOutput, long[] inputIds, int batchIndex, int seqLen)
        {
            const int HIDDEN_DIM = 768;
            float[] pooled = new float[HIDDEN_DIM];
            int validCount = 0;

            int batchOffset = batchIndex * seqLen * HIDDEN_DIM;

            for (int i = 0; i < seqLen; i++)
            {
                // 跳過 PAD (id=0)
                if (i >= inputIds.Length || inputIds[i] == 0) continue;

                validCount++;
                int tokenOffset = batchOffset + (i * HIDDEN_DIM);

                for (int j = 0; j < HIDDEN_DIM; j++)
                {
                    if (tokenOffset + j < flatOutput.Length)
                        pooled[j] += flatOutput[tokenOffset + j];
                }
            }

            // 平均與歸一化
            if (validCount > 0)
            {
                double norm = 0;
                for (int j = 0; j < HIDDEN_DIM; j++)
                {
                    pooled[j] /= validCount;
                    norm += pooled[j] * pooled[j];
                }

                // L2 Normalization
                norm = Math.Sqrt(norm);
                if (norm > 1e-12)
                {
                    for (int j = 0; j < HIDDEN_DIM; j++)
                        pooled[j] = (float)(pooled[j] / norm);
                }
            }

            return pooled;
        }
    }
}
