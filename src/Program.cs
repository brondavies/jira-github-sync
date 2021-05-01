using Atlassian.Jira;
using Newtonsoft.Json;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace jira_github_sync
{
    class Program
    {
        private static List<PullRequest> pullRequests;
        private static List<Atlassian.Jira.Issue> jiraIssues;
        private static Dictionary<string, PullRequest> issueAssociations = new Dictionary<string, PullRequest>();

        internal static string baseBranch => Config("baseBranch", "main");
        internal static string jiraServer => Config("jiraServer");
        internal static string repo => Config("githubRepo");
        internal static string repoOwner => Config("githubRepoOwner");
        internal static string localRepository => Config("localRepository");
        internal static string jiraPrefix => Config("jiraPrefix", "");
        internal static string jiraProductionReadyStatus => Config("jiraProductionReadyStatus", "Production Ready");
        internal static string pullRequestText => Config("pullRequestText", "Pull Request {number}");
        internal static string additionalLink => Config("additionalLink");
        internal static string additionalLinkText => Config("additionalLinkText");

        private static bool MergeDeployment => Config("MergeDeployment").ToBool();
        private static bool SearchDescription => Config("searchdescription").ToBool();
        private static bool SearchComments => Config("searchcomments").ToBool();

        static void Main(string[] args)
        {
            ReadArgs(args);
            if (ValidateConfig())
            {
                GetOpenPullRequests();
                GetJiraIssues();
                AssociateIssuesAndPRS();
                if (MergeDeployment)
                {
                    log("Warning: MergeDeployment is an experimental feature.");
                    CreateDeployment();
                }
            }
        }

        static string[] requiredKeys = new[] { "githubAccount", "githubToken", "githubRepoOwner", "githubRepo", "baseBranch", "jiraServer", "jiraUsername", "jiraPassword", "jiraPrefix" };
        private static bool ValidateConfig()
        {
            var valid = true;
            foreach (var key in requiredKeys)
            {
                if (Config(key).IsEmpty())
                {
                    log($"{key} was not configured and is a required value");
                    valid = false;
                }
            }
            return valid;
        }

        private static void CreateDeployment()
        {
            Environment.CurrentDirectory = localRepository;
            if (gitcmd($"checkout {baseBranch}") && gitcmd($"pull"))
            {
                var issues = GetJiraProductionReady();
                foreach (var issue in issues)
                {
                    var key = issue.Key.ToString();
                    if (issueAssociations.ContainsKey(key))
                    {
                        var pr = issueAssociations[key];
                        var branch = pr.Head.Ref;
                        log($"Branch {branch} for {key} is being merged");
                        if (!gitcmd($"merge origin/{branch}"))
                        {
                            log($"Failed to merge {branch}");
                            break;
                        }
                    }
                    else
                    {
                        log($"No PR found for {key}");
                    }
                }
            }
            else
            {
                log("Repository is not ready");
            }
        }

        private static bool gitcmd(string command)
        {
            log($"git {command}");
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = command,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            });
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();
            DataReceivedEventHandler redirect = (object sender, DataReceivedEventArgs e) =>
            {
                if (e.Data?.Trim().IsEmpty() == false)
                {
                    log(e.Data);
                }
            };
            proc.ErrorDataReceived += redirect;
            proc.OutputDataReceived += redirect;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }

        private static void AssociateIssuesAndPRS()
        {
            var jira = GetJiraClient();
            foreach(var id in issueAssociations.Keys)
            {
                var issue = jiraIssues.FirstOrDefault(i => i.Key == id);
                if (issue != null)
                {
                    var pr = issueAssociations[id];
                    EnsureIssueAssociation(issue, pr);
                }
                else
                {
                    log($"Could not find Jira issue {id}");
                }
            }
        }

        private static void EnsureIssueAssociation(Atlassian.Jira.Issue issue, PullRequest pr)
        {
            var link = $"github.com/{repoOwner}/{repo}/pull/{pr.Number}";
            if (!SearchDescription || false == issue.Description?.Contains(link, StringComparison.OrdinalIgnoreCase))
            {
                var comments = issue.GetCommentsAsync().Result;
                if (!SearchComments || !comments.Any(c => c.Body.Contains(link, StringComparison.OrdinalIgnoreCase)))
                {
                    var links = issue.GetRemoteLinksAsync().Result;
                    if (!links.Any(l => l.RemoteUrl.Contains(link)))
                    {
                        log($"Adding links to issue {issue.Key}");
                        if (!additionalLink.IsEmpty())
                        {
                            issue.AddRemoteLinkAsync(additionalLink.Tokenize(pr), additionalLinkText.Tokenize(pr)).Wait();
                        }
                        issue.AddRemoteLinkAsync($"https://{link}", pullRequestText.Tokenize(pr)).Wait();
                    }
                }
            }
        }

        private static void GetJiraIssues()
        {
            List<string> keys = issueAssociations.Keys.Distinct().ToList();
            Jira jira = GetJiraClient();

            // use LINQ syntax to retrieve issues
            IPagedQueryResult<Atlassian.Jira.Issue> issues = null;
            var success = false;
            do
            {
                try
                {
                    var jql = $"issueKey in ({string.Join(',', keys)})";
                    issues = jira.Issues.GetIssuesFromJqlAsync(jql, maxIssues: keys.Count).Result;
                    success = true;
                }
                catch(Exception e)
                {
                    if (e.Message.IsJiraIssue(out string[] errorKeys))
                    {
                        keys.RemoveAll(k => errorKeys.Contains(k));
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (!success);
            jiraIssues = issues?.ToList() ?? default;
        }

        private static List<Atlassian.Jira.Issue> GetJiraProductionReady()
        {
            Jira jira = GetJiraClient();

            var jql = $"status = \"{jiraProductionReadyStatus}\" and sprint in openSprints()";
            var issues = jira.Issues.GetIssuesFromJqlAsync(jql, maxIssues: 100).Result;
            return issues.ToList();
        }

        private static void EnsurePullRequestAssociation(PullRequest pr, string link)
        {
            var git = GetGithubClient();
            var comment = git.PullRequest.ReviewComment.GetAll(repoOwner, repo, pr.Number).Result.FirstOrDefault();
            if (pr.Body?.Contains(link, StringComparison.OrdinalIgnoreCase) == true || comment?.Body?.Contains(link, StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }
            log($"Updating description for PR {pr.Number}");
            git.PullRequest.Update(repoOwner, repo, pr.Number, new PullRequestUpdate { Body = $"{pr.Body}\r\n[{link}](https://{link})" }).Wait();
        }

        private static void GetOpenPullRequests()
        {
            var git = GetGithubClient();
            var request = new PullRequestRequest { State = ItemStateFilter.Open };
            var pulls = git.PullRequest.GetAllForRepository(repoOwner, repo, request);
            pullRequests = pulls.Result.ToList();

            foreach (var pr in pullRequests)
            {
                var comments = git.PullRequest.ReviewComment.GetAll(repoOwner, repo, pr.Number).Result.OrderBy(c => c.CreatedAt).ToList();
                if (!pr.Title.IsJiraIssue(out string[] JiraIds))
                {
                    if (!pr.Head.Ref.IsJiraIssue(out JiraIds))
                    {
                        var comment = comments.FirstOrDefault(c => c.Body.IsJiraIssue());
                        if (comment != null)
                        {
                            comment.Body.IsJiraIssue(out JiraIds);
                        }
                    }
                }

                if (JiraIds.Length > 0)
                {
                    foreach (var id in JiraIds)
                    {
                        EnsurePullRequestAssociation(pr, $"{jiraServer}/browse/{id}");
                        issueAssociations[id] = pr;
                    }
                }
                else
                {
                    log($"No issue ID found for PR {pr.Number}: {pr.Title}");
                }
            }
        }

        private static Jira _jiraClient;
        private static Jira GetJiraClient() => _jiraClient ?? (_jiraClient = Jira.CreateRestClient($"https://{jiraServer}", Config("jiraUsername"), Config("jiraPassword")));

        private static GitHubClient _githubClient;
        private static GitHubClient GetGithubClient() => _githubClient ?? (_githubClient = new GitHubClient(new ProductHeaderValue(Config("githubAccount")), new GithubCredentials(Config("githubToken"))));

        #region Boilerplate code

        private static string startupDirectory => Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private static Dictionary<string, string> configValues = new Dictionary<string, string>();
        private static Dictionary<string, string> _appSettings = null;
        private static Dictionary<string, string> AppSettings => _appSettings ?? GetAppSettings();

        private static Dictionary<string, string> GetAppSettings()
        {
            return
            _appSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(
                    Path.Combine(startupDirectory, "jira-github-sync.json")));
        }

        private static string Config(string name, string defaultValue = null)
        {
            if (configValues != null && configValues.ContainsKey(name))
            {
                return configValues[name];
            }
            if (AppSettings.ContainsKey(name))
            {
                return AppSettings[name];
            }
            return Env(name, defaultValue);
        }

        private static string Env(string name, string defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return value.IsEmpty() ? defaultValue : value;
        }

        private static void ReadArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var key = args[i].Replace("-", "");
                i++;
                var value = args[i];
                configValues[key] = value;
            }
        }

        private static void log(string message)
        {
            Console.WriteLine(message);
        }

        #endregion
    }
}
