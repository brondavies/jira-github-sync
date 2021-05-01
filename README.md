# jira-github-sync
A .NET Core command-line utility to keep your Jira issues automatically linked with the related PRs on Github

## Setup

### Pre-requisites

The build of jira-github-sync.exe relies on the .NET Core runtime by default. Install this first.
Currently [3.1](https://dotnet.microsoft.com/download/dotnet/3.1) is the target framework version.

### Github

- Create a personal access token for API use at https://github.com/settings/tokens It should grant the following access permissions:
  - repo
  - workflow

### Atlassian Jira

- Create an API token at https://id.atlassian.com/manage-profile/security/api-tokens
- Determine the server name of your hosted Jira instance; usually this is something like myaccount.atlassian.net
- Get the project key used in your Jira issues like `ABC` in issue# ABC-1234. This is the key listed at &lt;myaccount&gt;.atlassian.net/jira/projects

### Configuration

This application is command-line driven and does not prompt for any information.  
You can configure it in three ways.  The application will read the configuration values from these sources with the first one found being used

- Command line option in the form of `-keyName "key value"`
- Configuration file entry in `jira-github-sync.json`
- Environment variable name matching the keyName

An explanation of each setting can be found in [jira-github-sync.json](src/jira-github-sync.json)

## Running

You can run the application as part of your build process or CI pipeline or on a schedule.  It will search open PRs and Jira issues in the active sprint.
Any issues or PRs that do not have corresponding links will be updated.
