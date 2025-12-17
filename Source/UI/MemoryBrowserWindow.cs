using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Service;
using UnityEngine;
using Verse;

namespace RimTalk.UI
{
    /// <summary>
    /// [NEW] 記憶瀏覽器視窗
    /// 用於檢視與管理角色的三層記憶 (Short/Medium/Long Term)。
    /// </summary>
    public class MemoryBrowserWindow : Window
    {
        private readonly Pawn _pawn;
        private Vector2 _scrollPosition = Vector2.zero;
        private MemoryTab _currentTab = MemoryTab.ShortTerm;
        // 編輯狀態緩存
        private MemoryRecord _editingItem = null;
        private string _editBufferSummary = "";
        private string _editBufferKeywords = "";
        private int _editBufferImportance;
        private enum MemoryTab
        {
            ShortTerm,
            MediumTerm,
            LongTerm
        }
        public MemoryBrowserWindow(Pawn pawn)
        {
            _pawn = pawn;
            doCloseX = true;
            closeOnClickedOutside = true;
            draggable = true;
            resizeable = true;
        }
        public override Vector2 InitialSize => new(700f, 600f);
        public override void DoWindowContents(Rect inRect)
        {
            // 標題區域
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30f), "RimTalk.MemoryBrowser.Title".Translate(_pawn.LabelShort));
            Text.Font = GameFont.Small;
            // 獲取當前角色的記憶資料
            var comp = Find.World.GetComponent<RimTalkWorldComponent>();
            // [FIX] Add 'memoryData == null' check to prevent NRE
            if (comp == null ||
                !comp.PawnMemories.TryGetValue(_pawn.thingIDNumber, out var memoryData) ||
                memoryData == null)
            {
                Widgets.Label(new Rect(0, 40f, inRect.width, 30f), "RimTalk.MemoryBrowser.NoData".Translate());
                return;
            }

            // [FIX] Ensure lists are initialized (defensive programming)
            memoryData.ShortTermMemories ??= [];
            memoryData.MediumTermMemories ??= [];
            memoryData.LongTermMemories ??= [];

            // 標籤頁切換
            Rect tabRect = new Rect(0, 40f, inRect.width, 30f);
            DrawTabs(tabRect, memoryData);
            // 內容列表區域
            Rect contentRect = new Rect(0, 80f, inRect.width, inRect.height - 80f);
            DrawContent(contentRect, memoryData);
        }
        /// <summary>
        /// 繪製分頁標籤與數量統計
        /// </summary>
        private void DrawTabs(Rect rect, PawnMemoryData data)
        {
            float tabWidth = rect.width / 3f;
            // 準備標籤文字 (含數量)
            string shortCount = $" ({data.ShortTermMemories?.Count ?? 0})";
            string mediumCount = $" ({data.MediumTermMemories?.Count ?? 0})";
            string longCount = $" ({data.LongTermMemories?.Count ?? 0})";
            // 短期記憶 Tab
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.ShortTerm".Translate() + shortCount))
            {
                _currentTab = MemoryTab.ShortTerm;
                _editingItem = null; // 切換分頁時重置編輯狀態
            }
            // 中期記憶 Tab
            if (Widgets.ButtonText(new Rect(rect.x + tabWidth, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.MediumTerm".Translate() + mediumCount))
            {
                _currentTab = MemoryTab.MediumTerm;
                _editingItem = null;
            }
            // 長期記憶 Tab
            if (Widgets.ButtonText(new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.LongTerm".Translate() + longCount))
            {
                _currentTab = MemoryTab.LongTerm;
                _editingItem = null;
            }
            // 高亮當前 Tab
            GUI.color = Color.yellow;
            float highlightX = (int)_currentTab * tabWidth;
            Widgets.DrawLineHorizontal(highlightX, rect.yMax, tabWidth);
            GUI.color = Color.white;
        }
        /// <summary>
        /// 繪製記憶列表內容
        /// </summary>
        private void DrawContent(Rect rect, PawnMemoryData data)
        {
            Widgets.DrawMenuSection(rect);
            Rect viewRect = rect.ContractedBy(10f);

            // 決定當前要顯示的列表
            List<MemoryRecord> targetList = _currentTab switch
            {
                MemoryTab.ShortTerm => data.ShortTermMemories,
                MemoryTab.MediumTerm => data.MediumTermMemories,
                MemoryTab.LongTerm => data.LongTermMemories,
                _ => null
            };
            if (targetList.NullOrEmpty())
            {
                Widgets.Label(viewRect, "RimTalk.MemoryBrowser.NoMemories".Translate());
                return;
            }
            // 計算滾動區域高度
            float listWidth = viewRect.width - 16f;
            float contentHeight = CalculateTotalHeight(targetList, listWidth);
            float virtualHeight = Mathf.Max(viewRect.height, contentHeight + 50f);
            Rect scrollRect = new Rect(0, 0, listWidth, virtualHeight);
            Widgets.BeginScrollView(viewRect, ref _scrollPosition, scrollRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(scrollRect);
            // 倒序遍歷與顯示 (最新的在上面)
            for (int i = targetList.Count - 1; i >= 0; i--)
            {
                var mem = targetList[i];
                DrawMemoryRecord(listing, mem, targetList, i);
                listing.Gap(5f);
            }
            listing.End();
            Widgets.EndScrollView();
        }
        /// <summary>
        /// 繪製單條記憶 (顯示或編輯模式)
        /// </summary>
        private void DrawMemoryRecord(Listing_Standard listing, MemoryRecord mem, List<MemoryRecord> list, int index)
        {
            // [FIX] Null check for memory item
            if (mem == null) return;

            bool isEditing = _editingItem == mem;
            if (isEditing)
            {
                // -- 編輯模式 --
                float editHeight = 280f;
                Rect editRect = listing.GetRect(editHeight);
                Widgets.DrawBoxSolid(editRect, new Color(0.2f, 0.2f, 0.25f, 0.5f));
                Listing_Standard editListing = new Listing_Standard();
                editListing.Begin(editRect.ContractedBy(5f));
                // 編輯摘要
                editListing.Label("Content:");
                Rect textRect = editListing.GetRect(80f);
                _editBufferSummary = Widgets.TextArea(textRect, _editBufferSummary);
                // 編輯關鍵字
                editListing.Label("Keywords (comma separated):");
                _editBufferKeywords = editListing.TextEntry(_editBufferKeywords);
                // 編輯重要性
                editListing.Label($"Importance: {_editBufferImportance}");
                _editBufferImportance = (int)editListing.Slider(_editBufferImportance, 1, 5);
                editListing.Gap(10f);
                // 按鈕列
                Rect btnRect = editListing.GetRect(24f);
                float btnWidth = 80f;
                // [Save]
                if (Widgets.ButtonText(new Rect(btnRect.x, btnRect.y, btnWidth, 24f), "RimTalk.UI.Save".Translate()))
                {
                    mem.Summary = _editBufferSummary;
                    // 分割並清理關鍵字
                    mem.Keywords = _editBufferKeywords
                        .Split(new[] { ',', '，' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                    mem.Importance = _editBufferImportance;
                    _editingItem = null; // 結束編輯
                }
                // [Cancel]
                if (Widgets.ButtonText(new Rect(btnRect.x + btnWidth + 10f, btnRect.y, btnWidth, 24f), "RimTalk.UI.Cancel".Translate()))
                {
                    _editingItem = null;
                }
                editListing.End();
            }
            else
            {
                // -- 顯示模式 --
                float summaryHeight = Text.CalcHeight(mem.Summary, listing.ColumnWidth);
                float totalHeight = summaryHeight + 50f; // Header(20) + Summary + Footer(20) + Padding
                Rect rect = listing.GetRect(totalHeight);
                Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.2f, 0.3f));
                // Header: Relative Time | Importance | Access Count
                // [Use public MemoryService.GetTimeAgo]
                string timeStr = MemoryService.GetTimeAgo(mem.CreatedTick);
                string headerInfo = $"{timeStr} | Importance: {mem.Importance} | Access: {mem.AccessCount}";

                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + 5, rect.y + 2, rect.width - 120, 20), headerInfo);
                GUI.color = Color.white;
                // Buttons: [Edit] [Delete]
                float btnX = rect.xMax - 65f;
                // [Delete]
                if (Widgets.ButtonText(new Rect(btnX, rect.y + 2, 60f, 20f), "RimTalk.UI.Delete".Translate()))
                {
                    list.RemoveAt(index);
                    return; // 刪除後直接返回，避免繼續繪製
                }
                btnX -= 65f;
                // [Edit]
                if (Widgets.ButtonText(new Rect(btnX, rect.y + 2, 60f, 20f), "RimTalk.UI.Edit".Translate()))
                {
                    _editingItem = mem;
                    _editBufferSummary = mem.Summary;
                    _editBufferKeywords = string.Join(", ", mem.Keywords ?? new List<string>());
                    _editBufferImportance = mem.Importance;
                }
                // Body: Summary
                Widgets.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, summaryHeight), mem.Summary);
                // Footer: Keywords
                GUI.color = new Color(0.6f, 0.6f, 1f); // Light blue
                string keys = string.Join(", ", mem.Keywords ?? new List<string>());
                Widgets.Label(new Rect(rect.x + 5, rect.yMax - 22, rect.width - 10, 20), $"[{keys}]");
                GUI.color = Color.white;
            }
        }
        private float CalculateTotalHeight(List<MemoryRecord> list, float width)
        {
            float totalHeight = 0f;
            foreach (var mem in list)
            {
                if (_editingItem == mem)
                {
                    totalHeight += 280f + 5f;
                }
                else
                {
                    float summaryHeight = Text.CalcHeight(mem.Summary, width);
                    totalHeight += summaryHeight + 50f + 5f;
                }
            }
            return totalHeight;
        }
    }
}
