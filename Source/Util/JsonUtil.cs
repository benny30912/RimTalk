using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RimTalk.Util
{
    public static class JsonUtil
    {
        public static string SerializeToJson<T>(T obj)
        {
            using var stream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(T));

            serializer.WriteObject(stream, obj);

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static T DeserializeFromJson<T>(string json)
        {
            // 先做清理
            string sanitizedJson = Sanitize(json, typeof(T));

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedJson));
                var serializer = new DataContractJsonSerializer(typeof(T));

                return (T)serializer.ReadObject(stream);
            }
            catch (Exception)
            {
                // 這裡保留原本的 log 行為，你原來是 Logger.Error 或 Log.Error 就照用
                Logger.Error($"Json deserialization failed for {typeof(T).Name}\n{json}");
                throw;
            }
        }

        public static string Sanitize(string text, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // 移除 ```json / ``` 標記
            string sanitized = text.Replace("```json", "").Replace("```", "").Trim();

            // 擷取第一個 { 或 [ 到 最後一個 } 或 ] 的區間
            int startIndex = sanitized.IndexOfAny(new[] { '{', '[' });
            int endIndex = sanitized.LastIndexOfAny(new[] { '}', ']' });

            if (startIndex >= 0 && endIndex > startIndex)
            {
                sanitized = sanitized.Substring(startIndex, endIndex - startIndex + 1).Trim();
            }
            else
            {
                // 連一對括號都湊不出來，直接放棄
                return string.Empty;
            }

            // 修正 ][ 或 }{ 之類黏在一起的情況
            if (sanitized.Contains("]["))
            {
                sanitized = sanitized.Replace("][", ",");
            }
            if (sanitized.Contains("}{"))
            {
                sanitized = sanitized.Replace("}{", "},{");
            }

            // 處理多包一層 { [ ... ] } 的情況
            if (sanitized.StartsWith("{") && sanitized.EndsWith("}"))
            {
                string innerContent = sanitized.Substring(1, sanitized.Length - 2).Trim();
                if (innerContent.StartsWith("[") && innerContent.EndsWith("]"))
                {
                    sanitized = innerContent;
                }
            }

            bool isEnumerable = typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string);

            // 目標型別是集合，但只有單一 { ... }，幫他包成陣列
            if (isEnumerable && sanitized.StartsWith("{"))
            {
                sanitized = $"[{sanitized}]";
            }

            // ★ 新增：如果是集合且長得像陣列，就嘗試「砍掉壞尾巴，保留完整元素」
            if (isEnumerable && sanitized.StartsWith("["))
            {
                sanitized = TrimBrokenArrayTail(sanitized);
            }

            return sanitized;
        }

        /// <summary>
        /// 專門處理「陣列尾巴壞掉」的情況：
        /// - 只保留前面結構完整的元素
        /// - 並補上一個 ] 讓整體變成合法的 JSON 陣列
        /// - 若完全找不到任何完整元素，回傳 "[]"
        /// </summary>
        private static string TrimBrokenArrayTail(string jsonArray)
        {
            if (string.IsNullOrWhiteSpace(jsonArray))
            {
                return jsonArray;
            }

            // 理論上 sanitize 後一定從 [ 開頭，但這裡還是保護一下
            int firstBracket = jsonArray.IndexOf('[');
            if (firstBracket < 0)
            {
                return jsonArray;
            }

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            int lastCompleteElementEnd = -1;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escaped)
                {
                    // 前一個字元是反斜線，這個字元不解讀為控制字元
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    if (inString)
                    {
                        escaped = true;
                    }
                    continue;
                }

                if (c == '"')
                {
                    // 進出字串
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    // 字串內的任何括號都不算結構
                    continue;
                }

                // 非字串狀態下才處理結構深度
                if (c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ']' || c == '}')
                {
                    depth--;

                    // 在 depth == 1 的時候遇到 }，代表一個「陣列裡的物件」剛好結束
                    if (depth == 1 && c == '}')
                    {
                        lastCompleteElementEnd = i;
                    }
                    // 在 depth 回到 0 的時候遇到 ]，代表整個陣列完整結束，可以直接用原字串
                    else if (depth == 0 && c == ']')
                    {
                        return jsonArray;
                    }
                }
            }

            // 如果沒有任何完整元素，就回傳空陣列
            if (lastCompleteElementEnd == -1)
            {
                return "[]";
            }

            // 從第一個 [ 之後，到最後一個完整元素的結束位置，把中間內容取出
            string prefix = jsonArray.Substring(0, firstBracket + 1); // 包含 [
            string elementsContent = jsonArray.Substring(firstBracket + 1,
                lastCompleteElementEnd - (firstBracket + 1) + 1);

            // 去掉尾端多餘空白與逗號（理論上不會有逗號，但保險）
            elementsContent = elementsContent.TrimEnd();
            if (elementsContent.EndsWith(","))
            {
                elementsContent = elementsContent.Substring(0, elementsContent.Length - 1);
            }

            // 重組為 [ <elements> ]
            return prefix + elementsContent + "]";
        }
    }
}
