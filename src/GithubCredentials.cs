using Octokit;
using System.Threading.Tasks;

namespace jira_github_sync
{
    public class GithubCredentials : Octokit.ICredentialStore
    {
        private string token;

        public GithubCredentials(string token)
        {
            this.token = token;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<Credentials> GetCredentials()
        {
            return new Octokit.Credentials(token: token, authenticationType: AuthenticationType.Bearer);
        }
#pragma warning restore CS1998
    }
}