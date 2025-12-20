using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
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
        // 在類別內加入靜態欄位
        private static bool _nativeLoaded = false;

        // 詞表與特殊 Token ID
        private Dictionary<string, int> _vocab = new Dictionary<string, int>();
        private int _unkId = 100;
        private int _clsId = 101;
        private int _sepId = 102;

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

                // 在 Initialize 方法開頭，try 區塊內的第一行加入：
                if (!_nativeLoaded)
                {
                    // 找到模型所在目錄 (Resources/Model/)，然後相對定位到 Resources/Native/
                    string nativeDllPath = Path.Combine(
                        Path.GetDirectoryName(modelPath),  // Resources/Model/
                        "..",                              // Resources/
                        "Native",                          // Resources/Native/
                        "onnxruntime.dll"
                    );
                    nativeDllPath = Path.GetFullPath(nativeDllPath); // 正規化路徑

                    if (File.Exists(nativeDllPath))
                    {
                        // 使用 Windows API 預先載入原生 DLL
                        var handle = LoadLibrary(nativeDllPath);
                        if (handle == IntPtr.Zero)
                        {
                            Log.Warning($"[RimTalk] Failed to preload native DLL: {nativeDllPath}");
                        }
                        else
                        {
                            Log.Message($"[RimTalk] Preloaded native DLL: {nativeDllPath}");
                        }
                    }
                    _nativeLoaded = true;
                }

                // 驗證模型檔案存在
                if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                {
                    Log.Warning($"[RimTalk] VectorService: Model not found at: {modelPath}");
                    return;
                }

                // 2. 設定 ONNX Session 選項
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                // 若 CPU 支援，可開啟多線程優化
                sessionOptions.InterOpNumThreads = 2; 
                sessionOptions.IntraOpNumThreads = 2;

                Log.Message($"[RimTalk] Loading model from: {modelPath}");
                _session = new InferenceSession(modelPath, sessionOptions);

                // 3. 加載詞表
                LoadVocabulary(vocabPath);

                _isInitialized = true;
                
                // 4. 預熱 (Warmup) - 強制 JIT 編譯與記憶體分配
                Task.Run(() => ComputeEmbedding("Warmup initialization text."));
                
                Log.Message("[RimTalk] VectorService: Initialization complete!");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] VectorService Init Failed: {ex}");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// [同步] 計算單一句子的向量 (768維)
        /// 注意：這會佔用 CPU 約 30-100ms，盡量不要在主執行緒頻繁呼叫。
        /// </summary>
        public float[] ComputeEmbedding(string text)
        {
            if (!_isInitialized || string.IsNullOrWhiteSpace(text)) return new float[768];

            lock (_inferenceLock)
            {
                return InternalInference(new List<string> { text }).First();
            }
        }

        /// <summary>
        /// [批次] 一次計算多個句子的向量
        /// 優勢：比呼叫多次 ComputeEmbedding 快得多。適合環境標籤生成。
        /// </summary>
        public List<float[]> ComputeEmbeddingsBatch(List<string> texts)
        {
            if (!_isInitialized || texts == null || texts.Count == 0) return new List<float[]>();

            lock (_inferenceLock)
            {
                return InternalInference(texts);
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
            // 由於 ComputeEmbedding 輸出的向量已經歸一化，這裡直接算內積即可
            // 如果不確定來源，建議保留除以 Norm 的步驟
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
                int maxLen = 128; // 模型限制

                // 1. 準備 Batch Tensors
                var inputIdsTensor = new DenseTensor<long>(new[] { batchSize, maxLen });
                var attentionMaskTensor = new DenseTensor<long>(new[] { batchSize, maxLen });
                var tokenTypeIdsTensor = new DenseTensor<long>(new[] { batchSize, maxLen });

                // 臨時存儲 inputIds 以供 Pooling 使用 (因為 Tensor 讀取較慢)
                long[][] batchInputIds = new long[batchSize][];

                // 2. Tokenize 每個句子並填入 Tensor
                for (int b = 0; b < batchSize; b++)
                {
                    int[] tokens = Tokenize(texts[b], maxLen);
                    batchInputIds[b] = new long[tokens.Length];

                    for (int i = 0; i < maxLen; i++)
                    {
                        if (i < tokens.Length)
                        {
                            inputIdsTensor[b, i] = tokens[i];
                            attentionMaskTensor[b, i] = (tokens[i] == 0) ? 0 : 1; // 0 is PAD
                            batchInputIds[b][i] = tokens[i];
                        }
                        else
                        {
                            // Padding
                            inputIdsTensor[b, i] = 0;
                            attentionMaskTensor[b, i] = 0;
                        }
                        tokenTypeIdsTensor[b, i] = 0;
                    }
                }

                // 3. 執行推論
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
                };

                using (var results = _session.Run(inputs))
                {
                    // 獲取最後一層隱藏狀態 (Last Hidden State): [Batch, SeqLen, HiddenSize]
                    var outputData = results.First().AsEnumerable<float>().ToArray();
                    
                    // 4. 對每個 Batch 進行 Mean Pooling
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
                // 發生錯誤時回傳空向量列表
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

            // flatOutput 是展平的陣列，需要計算 offset
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

        private int[] Tokenize(string text, int maxLen)
        {
            var tokens = new List<int> { _clsId };
            text = text.ToLowerInvariant();

            for (int i = 0; i < text.Length && tokens.Count < maxLen - 1; i++)
            {
                char c = text[i];
                if (IsChinese(c))
                {
                    string s = c.ToString();
                    tokens.Add(_vocab.ContainsKey(s) ? _vocab[s] : _unkId);
                }
                else if (!char.IsWhiteSpace(c))
                {
                    int start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && !IsChinese(text[i])) i++;
                    i--;
                    
                    string word = text.Substring(start, i - start + 1);
                    tokens.AddRange(WordPieceTokenize(word));
                }
            }

            tokens.Add(_sepId);
            return tokens.ToArray();
        }

        private IEnumerable<int> WordPieceTokenize(string word)
        {
            // 這裡保留你原始的 WordPiece 邏輯
            if (_vocab.TryGetValue(word, out int id)) return new[] { id };

            var subTokens = new List<int>();
            int start = 0;
            bool isBad = false;

            while (start < word.Length)
            {
                int end = word.Length;
                int curId = -1;
                while (start < end)
                {
                    string sub = word.Substring(start, end - start);
                    if (start > 0) sub = "##" + sub;
                    if (_vocab.TryGetValue(sub, out int foundId))
                    {
                        curId = foundId;
                        break;
                    }
                    end--;
                }
                if (curId == -1) { isBad = true; break; }
                subTokens.Add(curId);
                start = end;
            }
            return isBad ? new[] { _unkId } : subTokens;
        }

        private bool IsChinese(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || 
                   (c >= 0x3400 && c <= 0x4DBF) || 
                   (c >= 0x20000 && c <= 0x2A6DF);
        }

        private void LoadVocabulary(string vocabPath)
        {
            try
            {
                if (!File.Exists(vocabPath)) return;

                var lines = File.ReadAllLines(vocabPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(lines[i])) _vocab[lines[i]] = i;
                }

                if (_vocab.ContainsKey("[UNK]")) _unkId = _vocab["[UNK]"];
                if (_vocab.ContainsKey("[CLS]")) _clsId = _vocab["[CLS]"];
                if (_vocab.ContainsKey("[SEP]")) _sepId = _vocab["[SEP]"];
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk] Vocab Load Error: {ex.Message}");
            }
        }
    }
}
