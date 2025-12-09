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

        // 簡單估算高度
        int itemCount = _currentTab switch
        {
            MemoryTab.ShortTerm => history.Messages?.Count ?? 0,
            MemoryTab.MediumTerm => history.MediumTermMemories?.Count ?? 0,
            MemoryTab.LongTerm => history.LongTermMemories?.Count ?? 0,
            _ => 0
        };

        // 為編輯模式預留額外高度
        float itemBaseHeight = _currentTab == MemoryTab.ShortTerm ? 100f : 150f;
        float virtualHeight = Mathf.Max(viewRect.height, itemCount * itemBaseHeight + 200f);

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

            // 編輯模式 (ShortTerm 只簡單支援刪除，或者您可以添加編輯文本功能)
            // 這裡為了保持簡單，只做刪除按鈕

            string displayText = msg.Text;
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
                float editHeight = 180f;
                Rect editRect = listing.GetRect(editHeight);
                Widgets.DrawBoxSolid(editRect, new Color(0.2f, 0.2f, 0.25f, 0.5f));

                Listing_Standard editListing = new Listing_Standard();
                editListing.Begin(editRect.ContractedBy(5f));

                // 內容編輯
                editListing.Label("Content:");
                Rect textRect = editListing.GetRect(60f);
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
}