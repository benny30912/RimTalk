using System;
using System.Collections.Generic;

namespace RimTalk.Source.Memory;

/// <summary>
/// 關鍵詞表達式匹配器
/// 支援語法：| (OR), & (AND), () (分組)
/// 優先級：() > & > |
/// </summary>
internal static class KeywordMatcher
{
    /// <summary>
    /// 評估表達式是否匹配，並返回匹配的關鍵詞數量
    /// </summary>
    /// <param name="expression">關鍵詞表達式，如 "(戰鬥|受傷) & 緊急"</param>
    /// <param name="context">要匹配的上下文文本</param>
    /// <returns>(是否匹配, 匹配的關鍵詞數量)</returns>
    public static (bool matched, int matchCount) Evaluate(string expression, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(context))
            return (false, 0);

        expression = expression.Trim();

        // 簡單情況：無運算符，直接匹配（向後相容）
        if (!expression.Contains('|') && !expression.Contains('&') && !expression.Contains('('))
        {
            bool match = context.IndexOf(expression, StringComparison.OrdinalIgnoreCase) >= 0;
            return (match, match ? 1 : 0);
        }

        return ParseOrExpression(expression, context);
    }

    /// <summary>
    /// 解析 OR 表達式（最低優先級）
    /// </summary>
    private static (bool matched, int matchCount) ParseOrExpression(string expr, string context)
    {
        var parts = SplitByOperator(expr, '|');
        if (parts.Count == 1)
            return ParseAndExpression(expr, context);

        int totalMatch = 0;
        bool anyMatched = false;
        foreach (var part in parts)
        {
            var (matched, count) = ParseAndExpression(part.Trim(), context);
            if (matched)
            {
                anyMatched = true;
                totalMatch += count;
            }
        }
        return (anyMatched, totalMatch);
    }

    /// <summary>
    /// 解析 AND 表達式（高於 OR）
    /// </summary>
    private static (bool matched, int matchCount) ParseAndExpression(string expr, string context)
    {
        var parts = SplitByOperator(expr, '&');
        if (parts.Count == 1)
            return ParsePrimary(expr, context);

        int totalMatch = 0;
        foreach (var part in parts)
        {
            var (matched, count) = ParsePrimary(part.Trim(), context);
            if (!matched)
                return (false, 0);  // AND 需要全部匹配
            totalMatch += count;
        }
        return (true, totalMatch);
    }

    /// <summary>
    /// 解析基本單元（括號或關鍵詞）
    /// </summary>
    private static (bool matched, int matchCount) ParsePrimary(string expr, string context)
    {
        expr = expr.Trim();

        // 處理括號：確認是完整的外層括號
        if (expr.StartsWith("(") && expr.EndsWith(")"))
        {
            int depth = 0;
            bool isCompletePair = true;
            for (int i = 0; i < expr.Length - 1; i++)
            {
                if (expr[i] == '(') depth++;
                else if (expr[i] == ')') depth--;
                if (depth == 0) { isCompletePair = false; break; }
            }
            if (isCompletePair)
                return ParseOrExpression(expr.Substring(1, expr.Length - 2), context);
        }

        // 純關鍵詞匹配（不區分大小寫）
        bool match = context.IndexOf(expr, StringComparison.OrdinalIgnoreCase) >= 0;
        return (match, match ? 1 : 0);
    }

    /// <summary>
    /// 按運算符分割字串，尊重括號層級
    /// </summary>
    private static List<string> SplitByOperator(string expr, char op)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == op && depth == 0)
            {
                result.Add(expr.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(expr.Substring(start));
        return result;
    }
}
