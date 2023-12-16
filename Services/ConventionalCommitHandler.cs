using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.Services;

public class ConventionalCommitHandler : ICommitHandler
{
    public ConventionalCommit? Parse(Commit commit)
    {
        if (string.IsNullOrEmpty(commit.MessageShort))
            return null;
        var shortMessageParts = commit.MessageShort.Split(':');
        if (shortMessageParts.Length <= 1)
            return null;
        var firstIndexOfParenthesis = shortMessageParts[0].IndexOf('(');
        var lastIndexOfParenthesis = shortMessageParts[0].LastIndexOf(')');
        var scope = string.Empty;
        var type = shortMessageParts[0];
        if (type.EndsWith('!'))
            type = type.Substring(0, type.Length - 1);
        if (firstIndexOfParenthesis != -1 && lastIndexOfParenthesis != -1)
        {
            if (firstIndexOfParenthesis >= lastIndexOfParenthesis)
                return null;
            scope = shortMessageParts[0]
                .Substring(lastIndexOfParenthesis, lastIndexOfParenthesis - firstIndexOfParenthesis);
            type = shortMessageParts[0].Substring(0, firstIndexOfParenthesis);
        }
        var description = commit.MessageShort.Remove(0, shortMessageParts[0].Length + 1).Trim();
        var body = string.Empty;
        if (!string.IsNullOrEmpty(commit.Message))
            body = commit.Message.Replace(commit.MessageShort, "").Trim();
        var isBreakingChange = body.Contains("BREAKING CHANGE:") || shortMessageParts[0].EndsWith('!');

        return new ConventionalCommit
        {
            Type = type,
            Scope = scope,
            Description = description,
            Body = body,
            IsBreakingChange = isBreakingChange
        };
    }
}