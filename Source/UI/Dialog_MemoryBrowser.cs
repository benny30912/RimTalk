using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_MemoryBrowser : Window
{
    private readonly Pawn _pawn;
    private Vector2 _scrollPosition = Vector2.zero;
    private MemoryTab _currentTab = MemoryTab.ShortTerm;

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

        // 標籤頁
        Rect tabRect = new Rect(0, 40f, inRect.width, 30f);
        DrawTabs(tabRect);

        // 內容區域
        Rect contentRect = new Rect(0, 80f, inRect.width, inRect.height - 80f);
        DrawContent(contentRect);
    }

    private void DrawTabs(Rect rect)
    {
        float tabWidth = rect.width / 3f;

        if (Widgets.ButtonText(new Rect(rect.x, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.ShortTerm".Translate()))
            _currentTab = MemoryTab.ShortTerm;

        if (Widgets.ButtonText(new Rect(rect.x + tabWidth, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.MediumTerm".Translate()))
            _currentTab = MemoryTab.MediumTerm;

        if (Widgets.ButtonText(new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, rect.height), "RimTalk.MemoryBrowser.LongTerm".Translate()))
            _currentTab = MemoryTab.LongTerm;

        // 當前標籤高亮 (底線)
        GUI.color = Color.yellow;
        float highlightX = (int)_currentTab * tabWidth;
        Widgets.DrawLineHorizontal(highlightX, rect.yMax, tabWidth);
        GUI.color = Color.white;
    }

    private void DrawContent(Rect rect)
    {
        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == _pawn);

        if (history == null)
        {
            Widgets.Label(rect, "RimTalk.MemoryBrowser.NoHistory".Translate());
            return;
        }

        Widgets.DrawMenuSection(rect);
        Rect viewRect = rect.ContractedBy(10f);

        // 簡單估算高度，確保 ScrollView 能運作 (Listing_Standard 會自動處理佈局，但需要足夠的虛擬高度)
        int itemCount = _currentTab switch
        {
            MemoryTab.ShortTerm => history.Messages.Count,
            MemoryTab.MediumTerm => history.MediumTermMemories.Count,
            MemoryTab.LongTerm => history.LongTermMemories.Count,
            _ => 0
        };
        float virtualHeight = Mathf.Max(viewRect.height, itemCount * 150f + 100f);
        Rect scrollRect = new Rect(0, 0, viewRect.width - 16f, virtualHeight);

        Widgets.BeginScrollView(viewRect, ref _scrollPosition, scrollRect);
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(scrollRect);

        switch (_currentTab)
        {
            case MemoryTab.ShortTerm:
                DrawShortTerm(listing, history.Messages);
                break;
            case MemoryTab.MediumTerm:
                DrawMemories(listing, history.MediumTermMemories);
                break;
            case MemoryTab.LongTerm:
                DrawMemories(listing, history.LongTermMemories);
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

        // 倒序顯示 (最新的在上面)
        foreach (var msg in Enumerable.Reverse(messages))
        {
            float textHeight = Text.CalcHeight(msg.Text, listing.ColumnWidth);
            float totalHeight = textHeight + 28f;

            Rect rect = listing.GetRect(totalHeight);
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.2f));

            string roleLabel = msg.Role == Role.User ? "Context" : "Dialogue";
            GUI.color = msg.Role == Role.User ? new Color(0.7f, 0.7f, 1f) : new Color(0.7f, 1f, 0.7f);
            Widgets.Label(new Rect(rect.x + 5, rect.y + 2, rect.width, 20), roleLabel);
            GUI.color = Color.white;

            Widgets.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, textHeight), msg.Text);

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

        foreach (var mem in Enumerable.Reverse(memories))
        {
            float summaryHeight = Text.CalcHeight(mem.Summary, listing.ColumnWidth);
            float totalHeight = summaryHeight + 50f; // Header(20) + Summary + Keywords(20) + Padding

            Rect rect = listing.GetRect(totalHeight);
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.2f, 0.3f));

            // Header: 時間 | 重要性 | 存取次數
            string timeStr = CommonUtil.GetTimeAgo(mem.CreatedTick);
            string header = $"{timeStr} | Importance: {mem.Importance} | Access: {mem.AccessCount}";
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 5, rect.y + 2, rect.width, 20), header);
            GUI.color = Color.white;

            // Body: 摘要
            Widgets.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, summaryHeight), mem.Summary);

            // Footer: 關鍵字
            GUI.color = new Color(0.6f, 0.6f, 1f);
            string keys = string.Join(", ", mem.Keywords ?? []);
            Widgets.Label(new Rect(rect.x + 5, rect.yMax - 22, rect.width - 10, 20), $"[{keys}]");
            GUI.color = Color.white;

            listing.Gap(5f);
        }
    }
}