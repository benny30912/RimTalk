using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// Context 向量建構器
    /// 收集動態情境項目，組合成 Context 向量用於記憶檢索。
    /// </summary>
    public class ContextVectorBuilder
    {
        // 在類別開頭的欄位宣告中加入：
        private readonly HashSet<string> _collectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 收集的動態項目向量
        private readonly List<float[]> _collectedVectors = new List<float[]>();

        /// <summary>
        /// 加入 Def 向量（透過快取）
        /// </summary>
        public void AddDef(Def def)
        {
            if (def == null) return;
            var vector = SemanticCache.Instance.GetVectorForDef(def);
            if (vector != null)
                _collectedVectors.Add(vector);
        }

        /// <summary>
        /// 加入多個 Def 向量
        /// </summary>
        public void AddDefs(IEnumerable<Def> defs)
        {
            if (defs == null) return;
            foreach (var def in defs)
                AddDef(def);
        }

        /// <summary>
        /// 加入固定文本向量（透過快取）
        /// </summary>
        public void AddText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var vector = SemanticCache.Instance.GetVectorForText(text);
            if (vector != null)
                _collectedVectors.Add(vector);
        }

        /// <summary>
        /// 加入心情（轉換為固定描述後加入）
        /// </summary>
        public void AddMood(float moodPercent)
        {
            string semantic = SemanticMapper.MapMoodToSemantic(moodPercent);
            AddText(semantic);
        }

        /// <summary>
        /// 加入事件性 Hediff（過濾後加入）
        /// </summary>
        public void AddEventHediffs(IEnumerable<Hediff> hediffs)
        {
            var eventHediffs = SemanticMapper.FilterEventHediffs(hediffs);
            foreach (var hediff in eventHediffs)
            {
                if (hediff?.def != null)
                    AddDef(hediff.def);
            }
        }

        /// <summary>
        /// 加入 Thoughts（個別向量從快取取得）
        /// </summary>
        public void AddThoughts(IEnumerable<Thought> thoughts)
        {
            if (thoughts == null) return;
            foreach (var thought in thoughts)
            {
                if (thought?.def != null)
                    AddDef(thought.def);
            }
        }

        /// <summary>
        /// 加入 Relations（提取關係詞彙後加入）
        /// </summary>
        public void AddRelations(string relationsText)
        {
            var relationWords = SemanticMapper.ExtractRelationWords(relationsText);
            foreach (var word in relationWords)
                AddText(word);
        }

        /// <summary>
        /// 加入 Surrounding（模擬聯想）
        /// </summary>
        public void AddSurrounding(List<string> labels)
        {
            string label = SemanticMapper.GetSurroundingLabel(labels);
            if (!string.IsNullOrEmpty(label))
                AddText(label);
        }

        /// <summary>
        /// 加入天氣
        /// </summary>
        public void AddWeather(WeatherDef weather)
        {
            if (weather != null)
                AddDef(weather);
        }

        /// <summary>
        /// 加入季節
        /// </summary>
        public void AddSeason(Season season)
        {
            // Season 是 enum，轉為文本
            string seasonText = season.Label();
            AddText(seasonText);
        }

        /// <summary>
        /// 加入位置/房間
        /// </summary>
        public void AddLocation(string locationText)
        {
            AddText(locationText);
        }

        public void AddTemperature(float celsius)
        {
            string semantic = SemanticMapper.MapTemperatureToSemantic(celsius);
            AddText(semantic);
        }

        /// <summary>
        /// 加入人名（用於人名加分）
        /// </summary>
        public void AddName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _collectedNames.Add(name);
        }

        /// <summary>
        /// 加入多個人名
        /// </summary>
        public void AddNames(IEnumerable<string> names)
        {
            if (names == null) return;
            foreach (var name in names)
                AddName(name);
        }
        /// <summary>
        /// 取得所有收集的人名
        /// </summary>
        public HashSet<string> GetAllNames()
        {
            return new HashSet<string>(_collectedNames, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 計算最終 Context 向量（所有收集向量的平均）
        /// </summary>
        public float[] Build()
        {
            if (_collectedVectors.Count == 0)
                return null;

            // 計算平均向量
            int dim = _collectedVectors[0].Length; // 768
            float[] result = new float[dim];

            foreach (var vec in _collectedVectors)
            {
                for (int i = 0; i < dim && i < vec.Length; i++)
                    result[i] += vec[i];
            }

            // 平均
            for (int i = 0; i < dim; i++)
                result[i] /= _collectedVectors.Count;

            // L2 歸一化
            double norm = 0;
            for (int i = 0; i < dim; i++)
                norm += result[i] * result[i];
            norm = Math.Sqrt(norm);

            if (norm > 1e-12)
            {
                for (int i = 0; i < dim; i++)
                    result[i] = (float)(result[i] / norm);
            }

            return result;
        }

        /// <summary>
        /// 取得所有收集的向量（用於 Max-Sim 計算）
        /// </summary>
        public List<float[]> GetAllVectors()
        {
            return _collectedVectors.ToList();
        }

        /// <summary>
        /// 取得已收集的向量數量
        /// </summary>
        public int Count => _collectedVectors.Count;

        /// <summary>
        /// 清除已收集的向量
        /// </summary>
        public void Clear()
        {
            _collectedVectors.Clear();
        }
    }
}
