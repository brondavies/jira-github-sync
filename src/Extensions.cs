using Octokit;
using RestSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jira_github_sync
{
    public static class Extensions
    {
        private static Regex JiraRegex = new Regex($@"(?<id>{Program.jiraPrefix}\W+\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        public static string Digits(this string value)
        {
            var digits = "";
            if (!string.IsNullOrEmpty(value))
            {
                foreach(var c in value)
                {
                    if (c >= 0x30 && c < 0x3A)
                    {
                        digits += c;
                    }
                }
            }
            return digits;
        }

        public static bool IsJiraIssue(this string value)
        {
            return value.IsJiraIssue(out string[] _);
        }

        public static bool IsJiraIssue(this string value, out string[] ids)
        {
            var result = false;
            var list = new List<string>();
            if (value.HasValue())
            {
                var matches = JiraRegex.Matches(value);
                foreach (var m in matches.ToList())
                {
                    if (m.Success)
                    {
                        var id = $"{Program.jiraPrefix}-" + m.Groups["id"].Value.Digits();
                        list.Add(id);
                        result = true;
                    }
                }
            }
            ids = list.ToArray();
            return result;
        }

        public static bool ToBool(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            return new[] { "true", "1", "on", "yes" }.Contains(value.ToLower());
        }

        public static string Tokenize(this string text, object values)
        {
            var result = text ?? "";
            if (!text.IsEmpty())
            {
                foreach (var prop in values.GetType().GetProperties())
                {
                    result = result.Replace("{" + prop.Name + "}", prop.GetValue(values)?.ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }
		    return result;
        }

        public static bool IsEmpty(this string value) => string.IsNullOrEmpty(value);
    }
}
