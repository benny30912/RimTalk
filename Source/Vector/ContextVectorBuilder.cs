using RimTalk.Data;
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
    /// [MOD] 改為收集 ContextItem，延遲向量計算到後台執行緒
    /// </summary>
    public class ContextVectorBuilder
    {
        // [MOD] 改為收集 ContextItem 而非立即計算向量
        private readonly List<ContextItem> _collectedItems = new List<ContextItem>();
        private readonly HashSet<string> _collectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// [NEW] 收集 Def 項目（延遲向量化）
        /// </summary>
        public void CollectDef(Def def)
        {
            if (def != null)
                _collectedItems.Add(new ContextItem { Type = ContextItem.ItemType.Def, Def = def });
        }

        /// <summary>
        /// [NEW] 收集多個 Def 項目
        /// </summary>
        public void CollectDefs(IEnumerable<Def> defs)
        {
            if (defs == null) return;
            foreach (var def in defs)
                CollectDef(def);
        }

        /// <summary>
        /// [NEW] 收集固定文本項目（延遲向量化）
        /// </summary>
        public void CollectText(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _collectedItems.Add(new ContextItem { Type = ContextItem.ItemType.Text, Text = text });
        }

        /// <summary>
        /// [NEW] 收集心情（轉換為語意描述後加入）
        /// </summary>
        public void CollectMood(float moodPercent)
        {
            string semantic = SemanticMapper.MapMoodToSemantic(moodPercent);
            CollectText(semantic);
        }

        /// <summary>
        /// [NEW] 收集事件性 Hediff
        /// </summary>
        public void CollectEventHediffs(IEnumerable<Hediff> hediffs)
        {
            var eventHediffs = SemanticMapper.FilterEventHediffs(hediffs);
            foreach (var hediff in eventHediffs)
            {
                if (hediff?.def != null)
                    CollectDef(hediff.def);
            }
        }

        /// <summary>
        /// [NEW] 收集 Thoughts
        /// </summary>
        public void CollectThoughts(IEnumerable<Thought> thoughts)
        {
            if (thoughts == null) return;
            foreach (var thought in thoughts)
            {
                if (thought?.def != null)
                    CollectDef(thought.def);
            }
        }

        /// <summary>
        /// 收集 Relations（同時提取關係詞彙和人名）
        /// </summary>
        public void CollectRelations(string relationsText)
        {
            if (string.IsNullOrEmpty(relationsText)) return;

            // 1. 收集關係詞彙
            var relationWords = SemanticMapper.ExtractRelationWords(relationsText);
            foreach (var word in relationWords)
                CollectText(word);

            // 2. 收集人名（用於人名加分）
            var relationNames = SemanticMapper.ExtractRelationNames(relationsText);
            AddNames(relationNames);
        }

        /// <summary>
        /// [MOD] 收集 Surrounding（改為接收 Thing 列表）
        /// </summary>
        public void CollectSurrounding(List<Thing> things)
        {
            string text = SemanticMapper.GetSurroundingText(things);
            if (!string.IsNullOrEmpty(text))
                CollectText(text);
        }

        /// <summary>
        /// [NEW] 收集天氣
        /// </summary>
        public void CollectWeather(WeatherDef weather)
        {
            if (weather != null)
                CollectDef(weather);
        }

        /// <summary>
        /// [NEW] 收集季節
        /// </summary>
        public void CollectSeason(Season season)
        {
            string seasonText = season.Label();
            CollectText(seasonText);
        }

        /// <summary>
        /// [NEW] 收集溫度
        /// </summary>
        public void CollectTemperature(float celsius)
        {
            string semantic = SemanticMapper.MapTemperatureToSemantic(celsius);
            CollectText(semantic);
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
        /// [NEW] 取得收集的項目清單（供批次計算）
        /// </summary>
        public List<ContextItem> GetCollectedItems()
        {
            return _collectedItems.ToList();
        }

        /// <summary>
        /// 清除已收集的項目
        /// </summary>
        public void Clear()
        {
            _collectedItems.Clear();
            _collectedNames.Clear();
        }
    }
}
