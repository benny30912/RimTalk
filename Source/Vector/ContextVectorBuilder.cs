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
    /// </summary>
    public class ContextVectorBuilder
    {
        private readonly List<ContextItem> _collectedItems = new();
        private readonly HashSet<string> _collectedNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 收集 Def 項目（帶類別標籤）
        /// </summary>
        public void CollectDef(Def def, ContextItem.Category category = ContextItem.Category.Other)
        {
            if (def != null && !string.IsNullOrWhiteSpace(def.label))
                _collectedItems.Add(new ContextItem
                {
                    Type = ContextItem.ItemType.Def,
                    Def = def,
                    ContextCategory = category
                });
        }

        /// <summary>
        /// 收集固定文本項目（帶類別標籤）
        /// </summary>
        public void CollectText(string text, ContextItem.Category category = ContextItem.Category.Other)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _collectedItems.Add(new ContextItem
                {
                    Type = ContextItem.ItemType.Text,
                    Text = text,
                    ContextCategory = category
                });
        }

        /// <summary>
        /// 收集心情
        /// </summary>
        public void CollectMood(float moodPercent)
        {
            string semantic = SemanticMapper.MapMoodToSemantic(moodPercent);
            CollectText(semantic, ContextItem.Category.Mood);
        }

        /// <summary>
        /// 收集事件性 Hediff
        /// </summary>
        public void CollectEventHediffs(IEnumerable<Hediff> hediffs)
        {
            var eventHediffs = SemanticMapper.FilterEventHediffs(hediffs);
            foreach (var hediff in eventHediffs)
            {
                if (hediff?.def != null)
                    CollectDef(hediff.def, ContextItem.Category.EventHediff);
            }
        }

        /// <summary>
        /// 收集 Thoughts
        /// </summary>
        public void CollectThoughts(IEnumerable<Thought> thoughts)
        {
            if (thoughts == null) return;
            foreach (var thought in thoughts)
            {
                if (thought == null) continue;
                string label = thought.LabelCap;
                if (!string.IsNullOrWhiteSpace(label))
                    CollectText(label, ContextItem.Category.Thought);
            }
        }

        /// <summary>
        /// 收集 Relations
        /// </summary>
        public void CollectRelations(string relationsText)
        {
            if (string.IsNullOrEmpty(relationsText)) return;

            var relationWords = SemanticMapper.ExtractRelationWords(relationsText);
            foreach (var word in relationWords)
                CollectText(word, ContextItem.Category.Relation);

            var relationNames = SemanticMapper.ExtractRelationNames(relationsText);
            AddNames(relationNames);
        }

        /// <summary>
        /// 收集 Surrounding
        /// </summary>
        public void CollectSurrounding(List<Thing> things)
        {
            string text = SemanticMapper.GetSurroundingText(things);
            if (!string.IsNullOrEmpty(text))
                CollectText(text, ContextItem.Category.Surrounding);
        }

        /// <summary>
        /// 收集天氣
        /// </summary>
        public void CollectWeather(WeatherDef weather)
        {
            if (weather != null)
                CollectDef(weather, ContextItem.Category.Weather);
        }

        /// <summary>
        /// 收集季節
        /// </summary>
        public void CollectSeason(Season season)
        {
            string seasonText = season.Label();
            CollectText(seasonText, ContextItem.Category.Season);
        }

        /// <summary>
        /// 收集溫度
        /// </summary>
        public void CollectTemperature(float celsius)
        {
            string semantic = SemanticMapper.MapTemperatureToSemantic(celsius);
            CollectText(semantic, ContextItem.Category.Temperature);
        }

        /// <summary>
        /// 收集時間
        /// </summary>
        public void CollectTime(string timeText)
        {
            CollectText(timeText, ContextItem.Category.Time);
        }

        /// <summary>
        /// 收集周圍活動
        /// </summary>
        public void CollectActivity(string activityText)
        {
            CollectText(activityText, ContextItem.Category.Activity);
        }

        /// <summary>
        /// 收集對話類型
        /// </summary>
        public void CollectDialogueType(string dialogueType)
        {
            CollectText(dialogueType, ContextItem.Category.DialogueType);
        }

        public void AddName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _collectedNames.Add(name);
        }

        public void AddNames(IEnumerable<string> names)
        {
            if (names == null) return;
            foreach (var name in names)
                AddName(name);
        }

        public HashSet<string> GetAllNames() => new(_collectedNames, StringComparer.OrdinalIgnoreCase);

        public List<ContextItem> GetCollectedItems() => _collectedItems.ToList();
    }
}
