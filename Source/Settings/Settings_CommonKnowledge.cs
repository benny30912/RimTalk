using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Source.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    // Common Knowledge UI state
    private Vector2 _ckListScrollPosition = Vector2.zero;
    private string _ckKeywordsBuffer = "";
    private string _ckContentBuffer = "";
    private MemoryRecord _selectedCkData = null; // 改用 MemoryRecord
    private int _ckImportanceBuffer = 3; // [NEW] Buffer for importance

    private void DrawCommonKnowledgeSettings(Rect rect)
    {
        if (Current.Game == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "RimTalk.Settings.CKStoredInSave".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        var worldComp = Find.World.GetComponent<RimTalkWorldComponent>();
        if (worldComp == null) return;
        var ckList = worldComp.CommonKnowledgeStore; // 這是 List<MemoryRecord>

        // 定義左右分割
        float gap = 10f;
        float rightWidth = rect.width * 0.35f;
        float leftWidth = rect.width - rightWidth - gap;

        Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
        Rect rightRect = new Rect(rect.x + leftWidth + gap, rect.y, rightWidth, rect.height);

        // --- 左側：列表 (Scroll View) ---
        Widgets.DrawMenuSection(leftRect);

        // [MOD] 動態計算每條常識的高度
        float CalculateItemHeight(MemoryRecord data, float width)
        {
            string keys = string.Join(", ", data.Keywords);
            string preview = $"[{keys}] (Imp:{data.Importance}) (Access:{data.AccessCount})\n{data.Summary}";
            float textHeight = Text.CalcHeight(preview, width - 35f);
            return Mathf.Max(50f, textHeight + 16f);  // 最小 50f，加上 padding
        }

        float listWidth = leftRect.width - 16f;
        float viewHeight = ckList.Sum(data => CalculateItemHeight(data, listWidth));
        Rect viewRect = new Rect(0, 0, listWidth, viewHeight);

        Widgets.BeginScrollView(leftRect, ref _ckListScrollPosition, viewRect);
        float currentY = 0f;

        for (int i = 0; i < ckList.Count; i++)
        {
            var data = ckList[i];
            float itemHeight = CalculateItemHeight(data, listWidth);  // [MOD] 動態高度
            Rect rowRect = new Rect(0, currentY, viewRect.width, itemHeight);

            // 背景高亮
            if (i % 2 == 0) Widgets.DrawLightHighlight(rowRect);
            if (data == _selectedCkData) Widgets.DrawHighlightSelected(rowRect);

            // [MOD] 改為多行顯示
            string keys = string.Join(", ", data.Keywords);
            string header = $"[{keys}] (Imp:{data.Importance}) (Access:{data.AccessCount})";
            string content = data.Summary;

            // Header（第一行）
            Rect headerRect = new Rect(rowRect.x + 5f, rowRect.y + 2f, rowRect.width - 35f, 20f);
            GUI.color = Color.gray;
            Widgets.Label(headerRect, header);
            GUI.color = Color.white;

            // Content（多行顯示）
            float contentHeight = itemHeight - 24f;
            Rect contentRect = new Rect(rowRect.x + 5f, rowRect.y + 22f, rowRect.width - 35f, contentHeight);
            Widgets.Label(contentRect, content);

            // 點擊選擇
            if (Widgets.ButtonInvisible(rowRect))
            {
                _selectedCkData = data;
                _ckKeywordsBuffer = string.Join(", ", data.Keywords);
                _ckContentBuffer = data.Summary;
                _ckImportanceBuffer = data.Importance;
            }

            // 刪除按鈕
            Rect deleteRect = new Rect(rowRect.xMax - 25f, rowRect.y + (itemHeight / 2) - 10f, 20f, 20f);
            if (Widgets.ButtonText(deleteRect, "✖"))
            {
                ckList.RemoveAt(i);
                if (_selectedCkData == data)
                {
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
                break;
            }

            currentY += itemHeight;
        }
        Widgets.EndScrollView();

        // --- 右側：編輯面板 ---
        Widgets.DrawMenuSection(rightRect);
        Rect innerRight = rightRect.ContractedBy(10f);

        Listing_Standard rightListing = new Listing_Standard();
        rightListing.Begin(innerRight);

        rightListing.Label(_selectedCkData == null ? "RimTalk.Settings.CKNewEntry".Translate() : "RimTalk.Settings.CKEditEntry".Translate());
        rightListing.Gap();

        rightListing.Label("RimTalk.Settings.CKKeywords".Translate());
        _ckKeywordsBuffer = rightListing.TextEntry(_ckKeywordsBuffer);

        // [NEW] 關鍵詞語法說明
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        rightListing.Label("RimTalk.Settings.CKKeywordsSyntaxHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        rightListing.Gap();

        rightListing.Label("RimTalk.Settings.CKContent".Translate());
        // TextArea 沒有直接在 Listing 中的 helper，用 GetRect 手動畫
        Rect contentAreaRect = rightListing.GetRect(200f);
        _ckContentBuffer = Widgets.TextArea(contentAreaRect, _ckContentBuffer);

        // [NEW] Importance Slider
        rightListing.Gap();
        rightListing.Label($"RimTalk.Settings.CKImportance".Translate() + $": {_ckImportanceBuffer}");
        // 這裡若無翻譯鍵可暫用英文 "Importance"
        _ckImportanceBuffer = (int)rightListing.Slider(_ckImportanceBuffer, 1, 5);

        rightListing.Gap(20f);

        string btnLabel = _selectedCkData == null ? "RimTalk.Settings.CKAdd".Translate() : "RimTalk.Settings.CKUpdate".Translate();
        if (rightListing.ButtonText(btnLabel))
        {
            if (!string.IsNullOrWhiteSpace(_ckKeywordsBuffer) && !string.IsNullOrWhiteSpace(_ckContentBuffer))
            {
                List<string> keys = _ckKeywordsBuffer.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (_selectedCkData != null)
                {
                    // Update existing
                    _selectedCkData.Keywords = keys;
                    _selectedCkData.Summary = _ckContentBuffer; // Map to Summary
                    _selectedCkData.Importance = _ckImportanceBuffer; // [NEW] Update Importance
                    // 清空選擇以便新增下一個
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                    _ckImportanceBuffer = 3; // Reset
                }
                else
                {
                    // Add new (Create MemoryRecord)
                    var newData = new MemoryRecord
                    {
                        Keywords = keys,
                        Summary = _ckContentBuffer,
                        Importance = _ckImportanceBuffer, // [MOD] Set Importance
                        CreatedTick = GenTicks.TicksGame
                    };
                    ckList.Add(newData);
                    // 清空
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                    _ckImportanceBuffer = 3; // Reset
                }
            }
        }

        // 如果正在編輯，給個取消按鈕
        if (_selectedCkData != null)
        {
            rightListing.Gap(5f);
            if (rightListing.ButtonText("RimTalk.Settings.CKCancelClear".Translate()))
            {
                _selectedCkData = null;
                _ckKeywordsBuffer = "";
                _ckContentBuffer = "";
            }
        }

        rightListing.End();
    }
}
