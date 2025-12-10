using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Service; // 新增：用於 PromptService
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_MemoryBrowser : Window
{
    private readonly Pawn _pawn;
    private Vector2 _scrollPosition = Vector2.zero;
    private MemoryTab _currentTab = MemoryTab.ShortTerm;

    // 編輯狀態
    private object _editingItem = null; // 當前正在編輯的物件 (MemoryRecord 或 TalkMessageEntry)
    private string _editBufferSummary = "";
    private string _editBufferKeywords = "";
    private int _editBufferImportance;

    private enum MemoryTab
    {
        ShortTerm,
        MediumTerm,
        LongTerm
    }

    public Dialog_MemoryBrowser(Pawn pawn)
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
        // 標題
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0, 0, inRect.width, 30f), "RimTalk.MemoryBrowser.Title".Translate(_pawn.LabelShort));
        Text.Font = GameFont.Small;

        // 獲取歷史紀錄以計算數量
        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == _pawn);

        // 標籤頁 (帶數量顯示)
        Rect tabRect = new Rect(0, 40f, inRect.width, 30f);
        DrawTabs(tabRect, history);

        // 內容區域
        Rect contentRect = new Rect(0, 80f, inRect.width, inRect.height - 80f);
        if (history == null)
        {
            Widgets.Label(contentRect, "RimTalk.MemoryBrowser.NoHistory".Translate());
        }
        else
        {
            DrawContent(contentRect, history);
        }
    }

    private void DrawTabs(Rect rect, PawnMessageHistoryRecord history)
    {
        float tabWidth = rect.width / 3f;

        // 計算數量字串
        string shortCount = history != null ? $" ({history.Messages?.Count ?? 0}/{TalkHistory.MaxMessages})" : "";
        string mediumCount = history != null ? $" ({history.MediumTermMemories?.Count ?? 0}/{TalkHistory.MaxMediumMemories})" : "";
        string longCount = history != null ? $" ({history.LongTermMemories?.Count ?? 0}/{TalkHistory.MaxLongMemories})" : "";

        if (Widgets.ButtonText(new Rect(rect.x, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.ShortTerm".Translate() + shortCount))
        {
            _currentTab = MemoryTab.ShortTerm;
            _editingItem = null; // 切換分頁時重置編輯狀態
        }

        if (Widgets.ButtonText(new Rect(rect.x + tabWidth, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.MediumTerm".Translate() + mediumCount))
        {
            _currentTab = MemoryTab.MediumTerm;
            _editingItem = null;
        }

        if (Widgets.ButtonText(new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.LongTerm".Translate() + longCount))
        {
            _currentTab = MemoryTab.LongTerm;
            _editingItem = null;
        }

        // 當前標籤高亮 (底線)
        GUI.color = Color.yellow;
        float highlightX = (int)_currentTab * tabWidth;
        Widgets.DrawLineHorizontal(highlightX, rect.yMax, tabWidth);
        GUI.color = Color.white;
    }

    private void DrawContent(Rect rect, PawnMessageHistoryRecord history)
    {
        Widgets.DrawMenuSection(rect);
        Rect viewRect = rect.ContractedBy(10f);

        // ★ 修改：先計算出實際的 listWidth (ScrollRect 的寬度)
        float listWidth = viewRect.width - 16f;

        // ★ 修改：使用精確計算的高度，而不是估算
        // 加上一些緩衝區 (Buffer) 以防萬一
        // ★ 修改：將正確的寬度傳入計算方法
        float contentHeight = CalculateTotalHeight(history, listWidth);
        float virtualHeight = Mathf.Max(viewRect.height, contentHeight + 50f);

        Rect scrollRect = new Rect(0, 0, viewRect.width - 16f, virtualHeight);

        Widgets.BeginScrollView(viewRect, ref _scrollPosition, scrollRect);
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(scrollRect);

        switch (_currentTab)
        {
            case MemoryTab.ShortTerm:
                DrawShortTerm(listing, history.Messages ?? new List<TalkMessageEntry>());
                break;
            case MemoryTab.MediumTerm:
                DrawMemories(listing, history.MediumTermMemories ?? new List<MemoryRecord>());
                break;
            case MemoryTab.LongTerm:
                DrawMemories(listing, history.LongTermMemories ?? new List<MemoryRecord>());
                break;
        }

        listing.End();
        Widgets.EndScrollView();
    }

    private void DrawShortTerm(Listing_Standard listing, List<TalkMessageEntry> messages)
    {
        if (messages.NullOrEmpty())
        {
            listing.Label("RimTalk.MemoryBrowser.NoRecentMessages".Translate());
            return;
        }

        // 倒序顯示，並允許刪除
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];

            // ★ 修改：呼叫共用方法
            string displayText = GetDisplayText(msg);
            string roleLabel = msg.Role == Role.User ? "Context" : "Dialogue";

            // ★ 修改：處理顯示文字，使其與 MemoryService 邏輯一致
            if (msg.Role == Role.User)
            {
                string extracted = PromptService.ExtractContextFromPrompt(msg.Text);
                displayText = string.IsNullOrWhiteSpace(extracted) ? "(No context)" : extracted.Trim();
            }
            else // Role.AI
            {
                try
                {
                    var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(msg.Text);
                    if (responses != null && responses.Any())
                    {
                        // ★ 修改：加入說話者名稱，並用換行分隔
                        displayText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                    }
                }
                catch
                {
                    // keep raw text
                }
            }

            // 計算高度 (這裡必須跟 CalculateTotalHeight 的邏輯保持一致)
            float textHeight = Text.CalcHeight(displayText, listing.ColumnWidth);
            float totalHeight = textHeight + 28f;

            Rect rect = listing.GetRect(totalHeight);
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.2f));

            GUI.color = msg.Role == Role.User ? new Color(0.7f, 0.7f, 1f) : new Color(0.7f, 1f, 0.7f);
            Widgets.Label(new Rect(rect.x + 5, rect.y + 2, rect.width - 30, 20), roleLabel);
            GUI.color = Color.white;

            // Delete button
            if (Widgets.ButtonText(new Rect(rect.xMax - 25, rect.y + 2, 20, 20), "X"))
            {
                messages.RemoveAt(i);
            }

            // Body
            Widgets.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, textHeight), displayText);

            listing.Gap(5f);
        }
    }

    private void DrawMemories(Listing_Standard listing, List<MemoryRecord> memories)
    {
        if (memories.NullOrEmpty())
        {
            listing.Label("RimTalk.MemoryBrowser.NoMemories".Translate());
            return;
        }

        for (int i = memories.Count - 1; i >= 0; i--)
        {
            var mem = memories[i];
            bool isEditing = _editingItem == mem;

            if (isEditing)
            {
                // 編輯模式介面
                float editHeight = 280f; // ★ 修改：增加編輯區域高度到 280f，確保下方按鈕可見
                Rect editRect = listing.GetRect(editHeight);
                Widgets.DrawBoxSolid(editRect, new Color(0.2f, 0.2f, 0.25f, 0.5f));

                Listing_Standard editListing = new Listing_Standard();
                editListing.Begin(editRect.ContractedBy(5f));

                // 內容編輯
                editListing.Label("Content:");
                Rect textRect = editListing.GetRect(80f); // 稍微加高文本框
                _editBufferSummary = Widgets.TextArea(textRect, _editBufferSummary);

                // 關鍵字編輯
                editListing.Label("Keywords (comma separated):");
                _editBufferKeywords = editListing.TextEntry(_editBufferKeywords);

                // 重要性編輯
                editListing.Label($"Importance: {_editBufferImportance}");
                _editBufferImportance = (int)editListing.Slider(_editBufferImportance, 1, 5);

                // 按鈕列
                Rect btnRect = editListing.GetRect(24f);
                float btnWidth = 80f;

                if (Widgets.ButtonText(new Rect(btnRect.x, btnRect.y, btnWidth, 24f), "RimTalk.UI.Save".Translate()))
                {
                    // 保存變更
                    mem.Summary = _editBufferSummary;
                    mem.Keywords = _editBufferKeywords.Split(new[] { ',', '，' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                    mem.Importance = _editBufferImportance;
                    _editingItem = null;
                }

                if (Widgets.ButtonText(new Rect(btnRect.x + btnWidth + 10f, btnRect.y, btnWidth, 24f), "RimTalk.UI.Cancel".Translate()))
                {
                    _editingItem = null;
                }

                editListing.End();
            }
            else
            {
                // 顯示模式
                float summaryHeight = Text.CalcHeight(mem.Summary, listing.ColumnWidth);
            float totalHeight = summaryHeight + 50f; // Header(20) + Summary + Keywords(20) + Padding

                Rect rect = listing.GetRect(totalHeight);
                Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.2f, 0.3f));

            // Header: 時間 | 重要性 | 存取次數
                string timeStr = CommonUtil.GetTimeAgo(mem.CreatedTick);
            string header = $"{timeStr} | Importance: {mem.Importance} | Access: {mem.AccessCount}";
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + 5, rect.y + 2, rect.width - 120, 20), header);
                GUI.color = Color.white;

                // Edit/Delete Buttons in Header
                float btnX = rect.xMax - 65f;
                if (Widgets.ButtonText(new Rect(btnX, rect.y + 2, 60f, 20f), "RimTalk.UI.Delete".Translate()))
                {
                    memories.RemoveAt(i);
                    continue; // 刪除後直接跳過繪製
                }

                btnX -= 65f;
                if (Widgets.ButtonText(new Rect(btnX, rect.y + 2, 60f, 20f), "RimTalk.UI.Edit".Translate()))
                {
                    _editingItem = mem;
                    _editBufferSummary = mem.Summary;
                    _editBufferKeywords = string.Join(", ", mem.Keywords ?? []);
                    _editBufferImportance = mem.Importance;
                }

                // Body
                Widgets.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, summaryHeight), mem.Summary);

            // Footer: 關鍵字
                GUI.color = new Color(0.6f, 0.6f, 1f);
                string keys = string.Join(", ", mem.Keywords ?? []);
                Widgets.Label(new Rect(rect.x + 5, rect.yMax - 22, rect.width - 10, 20), $"[{keys}]");
                GUI.color = Color.white;
            }

            listing.Gap(5f);
        }
    }

    // 新增：統一處理文本提取，避免重複邏輯
    private string GetDisplayText(TalkMessageEntry msg)
    {
        if (msg.Role == Role.User)
        {
            string extracted = PromptService.ExtractContextFromPrompt(msg.Text);
            return string.IsNullOrWhiteSpace(extracted) ? "(No context)" : extracted.Trim();
        }
        else
        {
            try
            {
                // 簡單緩存或直接解析，考慮到只有30條，直接解析效能尚可
                // 如果想要極致效能，可以在 TalkMessageEntry 裡加個緩存欄位
                var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(msg.Text);
                if (responses != null && responses.Any())
                {
                    return string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                }
            }
            catch { }
            return msg.Text; // 解析失敗則顯示原始文本
        }
    }

    // ★ 修改：增加 width 參數，並移除內部的寫死寬度
    private float CalculateTotalHeight(PawnMessageHistoryRecord history, float width)
    {
        if (history == null) return 0f;
        float totalHeight = 0f;

        if (_currentTab == MemoryTab.ShortTerm)
        {
            if (history.Messages == null) return 0f;
            foreach (var msg in history.Messages)
            {
                string text = GetDisplayText(msg);
                float textHeight = Text.CalcHeight(text, width);
                totalHeight += textHeight + 28f + 5f; // Header(28) + Gap(5)
            }
        }
        else // Medium & Long Term
        {
            var list = _currentTab == MemoryTab.MediumTerm ? history.MediumTermMemories : history.LongTermMemories;
            if (list == null) return 0f;

            foreach (var mem in list)
            {
                // 如果正在編輯這個項目，高度會不同
                if (_editingItem == mem)
                {
                    totalHeight += 280f + 5f; // EditHeight(280) + Gap(5)
                }
                else
                {
                    float summaryHeight = Text.CalcHeight(mem.Summary, width);
                    totalHeight += summaryHeight + 50f + 5f; // Header+Footer(50) + Gap(5)
                }
            }
        }

        return totalHeight;
    }
}