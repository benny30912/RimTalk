using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
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

        float itemHeight = 40f;
        float viewHeight = ckList.Count * itemHeight;
        Rect viewRect = new Rect(0, 0, leftRect.width - 16f, viewHeight);

        Widgets.BeginScrollView(leftRect, ref _ckListScrollPosition, viewRect);
        float currentY = 0f;

        for (int i = 0; i < ckList.Count; i++)
        {
            var data = ckList[i];
            Rect rowRect = new Rect(0, currentY, viewRect.width, itemHeight);

            // 背景高亮
            if (i % 2 == 0) Widgets.DrawLightHighlight(rowRect);
            if (data == _selectedCkData) Widgets.DrawHighlightSelected(rowRect);

            // 內容預覽
            string keys = string.Join(", ", data.Keywords);
            // 適配 MemoryRecord: Content -> Summary
            string preview = $"[{keys}] {data.Summary}";

            Rect textRect = new Rect(rowRect.x + 5f, rowRect.y, rowRect.width - 30f, rowRect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(textRect, preview);
            Text.Anchor = TextAnchor.UpperLeft;

            // 點擊選擇
            if (Widgets.ButtonInvisible(textRect))
            {
                _selectedCkData = data;
                _ckKeywordsBuffer = string.Join(", ", data.Keywords);
                _ckContentBuffer = data.Summary;
            }

            // 刪除按鈕
            Rect deleteRect = new Rect(rowRect.xMax - 25f, rowRect.y + 10f, 20f, 20f);
            if (Widgets.ButtonText(deleteRect, "✖"))
            {
                ckList.RemoveAt(i);
                if (_selectedCkData == data)
                {
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
                break; // 重繪列表
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

        rightListing.Gap();

        rightListing.Label("RimTalk.Settings.CKContent".Translate());
        // TextArea 沒有直接在 Listing 中的 helper，用 GetRect 手動畫
        Rect contentAreaRect = rightListing.GetRect(200f);
        _ckContentBuffer = Widgets.TextArea(contentAreaRect, _ckContentBuffer);

        rightListing.Gap(20f);

        string btnLabel = _selectedCkData == null ? "RimTalk.Settings.CKAdd".Translate() : "RimTalk.Settings.CKUpdate".Translate();
        if (rightListing.ButtonText(btnLabel))
        {
            if (!string.IsNullOrWhiteSpace(_ckKeywordsBuffer) && !string.IsNullOrWhiteSpace(_ckContentBuffer))
            {
                List<string> keys = _ckKeywordsBuffer.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (_selectedCkData != null)
                {
                    // Update existing
                    _selectedCkData.Keywords = keys;
                    _selectedCkData.Summary = _ckContentBuffer; // Map to Summary
                    // 清空選擇以便新增下一個
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
                else
                {
                    // Add new (Create MemoryRecord)
                    var newData = new MemoryRecord
                    {
                        Keywords = keys,
                        Summary = _ckContentBuffer,
                        Importance = 3, // 預設重要性
                        CreatedTick = GenTicks.TicksGame
                    };
                    ckList.Add(newData);
                    // 清空
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
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
