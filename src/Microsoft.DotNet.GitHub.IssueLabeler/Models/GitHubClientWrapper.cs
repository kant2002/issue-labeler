using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.IssueLabeler.Data;
using Octokit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface IGitHubClientWrapper
    {
        Task<Octokit.Issue> GetIssue(string owner, string repo, int number);
        Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number);
        Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number);
        Task<bool> AddCardToProject(string owner, string repo);
        Task MoveFromUncommittedToFuture(string owner, string repo);
        Task AddToPrColumn(string owner, string repo);
        Task AddMissingTriagedFuture(string owner, string repo);
        Task AddMissingTriaged6_0(string owner, string repo);
    }

    public class GitHubClientWrapper : IGitHubClientWrapper
    {
        private readonly ILogger<GitHubClientWrapper> _logger;
        private GitHubClient _client;
        private readonly GitHubClientFactory _gitHubClientFactory;
        private readonly bool _skipAzureKeyVault;

        public GitHubClientWrapper(
            ILogger<GitHubClientWrapper> logger,
            IConfiguration configuration,
            GitHubClientFactory gitHubClientFactory)
        {
            _skipAzureKeyVault = configuration.GetSection("SkipAzureKeyVault").Get<bool>(); // TODO locally true
            _gitHubClientFactory = gitHubClientFactory;
            _logger = logger;

        }

        // TODO add lambda to remove repetetive logic in this class
        // -> call and pass a lambda calls create, and if fails remake and call it again.

        public async Task<Octokit.Issue> GetIssue(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            Octokit.Issue iop = null;
            try
            {
                iop = await _client.Issue.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                iop = await _client.Issue.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task AddToPrColumn(string owner, string repo)
        {
            bool disable = false;
            if (disable)
                return;

            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

            var existingCards = await client.Repository.Project.Card.GetAll(12751161); // active prs
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var skipFor = new HashSet<int>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                if (myCard.ContentUrl == null)
                {
                    continue;
                }
                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    skipFor.Add(issueNumber);
                }
            }


            // get all untriaged issues in the repo
            // https://github.com/dotnet/corefx/pulls?q=is%3Aopen+is%3Apr+label%3Atenet-performance
            var sir = new SearchIssuesRequest()
            {
                Is = new[] { IssueIsQualifier.PullRequest },
                //Labels = new[] { "needs further triage" },
                State = ItemState.Open, // Closed to get closed ones
            };
            var acceptableLabels = new string[]
            {
                    "area-Extensions-Logging",
                    "area-Extensions-Options",
                    "area-Extensions-Primitives",
                    "area-Extensions-Caching",
                    "area-Extensions-Configuration",
                    "area-Extensions-Hosting",
                    "area-DependencyModel",
                    "area-Extensions-DependencyInjection",
                    "area-System.ComponentModel",
                    "area-System.Drawing",
                    "area-System.Globalization",
            }.ToHashSet();
            sir.Repos.Add(owner, repo);
            sir.Page = 1; // TODO: rerun with higher page number if more exists
            _logger.LogInformation("sir.Page: " + sir.Page);
            foreach (var areaLabel in acceptableLabels)
            {
                sir.Labels = new[] { areaLabel };

                var xx = await client.Search.SearchIssues(sir);
                bool addIssue = false;
                foreach (var item in xx.Items)
                {
                    if (skipFor.Contains(item.Number))
                        continue;

                    foreach (var label in item.Labels)
                    {
                        if (acceptableLabels.Contains(label.Name))
                        {
                            //if (item.Milestone != null)
                            //if (item.Milestone != null)
                            {
                                addIssue = true;
                                break;
                            }
                        }
                    }

                    if (addIssue)
                    {
                        addIssue = false;
                        skipFor.Add(item.Number);
                        var pr = await client.PullRequest.Get(owner, repo, item.Number);

                        var newCard = new NewProjectCard(pr.Id, ProjectCardContentType.PullRequest);
                        try
                        {
                            var result = await client.Repository.Project.Card.Create(12751161, newCard);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"#{item.Number}: ex was of type {ex.GetType()}, message: {ex.Message}");
                        }
                    }
                }
            }
        }

        public async Task AddMissingTriaged6_0(string owner, string repo)
        {
            bool disable = true;
            if (disable)
                return;

            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

            var existingCards = await client.Repository.Project.Card.GetAll(9664733); // 6.0.0
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var skipFor = new HashSet<int>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                if (myCard.ContentUrl == null)
                {
                    continue;
                }
                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    skipFor.Add(issueNumber);
                }
            }


            // get all untriaged issues in the repo
            // https://github.com/dotnet/corefx/pulls?q=is%3Aopen+is%3Apr+label%3Atenet-performance
            var sir = new SearchIssuesRequest()
            {
                Is = new[] { IssueIsQualifier.Issue },
                //Labels = new[] { "needs further triage" },
                State = ItemState.Open, // Closed to get closed ones
            };
            var acceptableLabels = new string[]
            {
                    "area-Extensions-Logging",
                    "area-Extensions-Options",
                    "area-Extensions-Primitives",
                    "area-Extensions-Caching",
                    "area-Extensions-Configuration",
                    "area-Extensions-Hosting",
                    "area-DependencyModel",
                    "area-Extensions-DependencyInjection",
                    "area-System.ComponentModel",
                    "area-System.Drawing",
                    "area-System.Globalization",
            }.ToHashSet();
            sir.Repos.Add(owner, repo);
            sir.Page = 1; // TODO: rerun with higher page number if more exists
            _logger.LogInformation("sir.Page: " + sir.Page);
            foreach (var areaLabel in acceptableLabels)
            {
                sir.Labels = new[] { areaLabel };

                var xx = await client.Search.SearchIssues(sir);
                bool addIssue = false;
                foreach (var item in xx.Items)
                {
                    if (skipFor.Contains(item.Number) ||
                        item.Labels.Select(x => x.Name).Contains("untriaged") ||
                        item.Labels.Select(x => x.Name).Contains("needs further triage"))
                        continue;

                    foreach (var label in item.Labels)
                    {
                        if (acceptableLabels.Contains(label.Name))
                        {
                            if (item.Milestone != null && item.Milestone.Title.Equals("6.0.0"))
                            {
                                addIssue = true;
                                break;
                            }
                        }
                    }

                    if (addIssue)
                    {
                        addIssue = false;
                        skipFor.Add(item.Number);

                        var newCard = new NewProjectCard(item.Id, ProjectCardContentType.Issue);
                        try
                        {
                            var result = await client.Repository.Project.Card.Create(9664733, newCard);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"#{item.Number}: ex was of type {ex.GetType()}, message: {ex.Message}");
                        }
                    }
                }
            }
        }

        public async Task AddMissingTriagedFuture(string owner, string repo)
        {
            bool disable = true;
            if (disable)
                return;

            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

            var existingCards = await client.Repository.Project.Card.GetAll(12751157); // Future
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var skipFor = new HashSet<int>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                if (myCard.ContentUrl == null)
                {
                    continue;
                }
                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    skipFor.Add(issueNumber);
                }
            }


            // get all untriaged issues in the repo
            // https://github.com/dotnet/corefx/pulls?q=is%3Aopen+is%3Apr+label%3Atenet-performance
            var sir = new SearchIssuesRequest()
            {
                Is = new[] { IssueIsQualifier.Issue },
                //Labels = new[] { "needs further triage" },
                State = ItemState.Open, // Closed to get closed ones
            };
            var acceptableLabels = new string[]
            {
                    "area-Extensions-Logging",
                    "area-Extensions-Options",
                    "area-Extensions-Primitives",
                    "area-Extensions-Caching",
                    "area-Extensions-Configuration",
                    "area-Extensions-Hosting",
                    "area-DependencyModel",
                    "area-Extensions-DependencyInjection",
                    "area-System.ComponentModel",
                    "area-System.Drawing",
                    "area-System.Globalization",
            }.ToHashSet();
            sir.Repos.Add(owner, repo);
            sir.Page = 1; // TODO: rerun with higher page number if more exists
            _logger.LogInformation("sir.Page: " + sir.Page);
            foreach (var areaLabel in acceptableLabels)
            {
                sir.Labels = new[] { areaLabel };

                var xx = await client.Search.SearchIssues(sir);
                bool addIssue = false;
                foreach (var item in xx.Items)
                {
                    if (skipFor.Contains(item.Number) ||
                        item.Labels.Select(x => x.Name).Contains("untriaged") ||
                        item.Labels.Select(x => x.Name).Contains("needs further triage"))
                        continue;

                    foreach (var label in item.Labels)
                    {
                        if (acceptableLabels.Contains(label.Name))
                        {
                            if (item.Milestone != null && item.Milestone.Title.Equals("Future"))
                            {
                                addIssue = true;
                                break;
                            }
                        }
                    }

                    if (addIssue)
                    {
                        addIssue = false;
                        skipFor.Add(item.Number);

                        var newCard = new NewProjectCard(item.Id, ProjectCardContentType.Issue);
                        try
                        {
                            var result = await client.Repository.Project.Card.Create(12751157, newCard);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"#{item.Number}: ex was of type {ex.GetType()}, message: {ex.Message}");
                        }
                    }
                }
            }
        }

        public async Task AddToPrColumnOld(string owner, string repo)
        {
            bool disable = true;
            if (disable)
                return;

            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

            var existingCards = await client.Repository.Project.Card.GetAll(12751161); // Active PRs
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var skipFor = new HashSet<int>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                if (myCard.ContentUrl == null)
                {
                    continue;
                }
                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    skipFor.Add(issueNumber);
                }
            }

            // get all untriaged issues in the repo
            // https://github.com/dotnet/corefx/pulls?q=is%3Aopen+is%3Apr+label%3Atenet-performance
            var sir = new SearchIssuesRequest()
            {
                Is = new[] { IssueIsQualifier.PullRequest },
                //Labels = new[] { "needs further triage" },
                State = ItemState.Open, // Closed to get closed ones
            };
            var acceptableLabels = new string[]
            {
                    "area-Extensions-Logging",
                    "area-Extensions-Options",
                    "area-Extensions-Primitives",
                    "area-Extensions-Caching",
                    "area-Extensions-Configuration",
                    "area-Extensions-Hosting",
                    "area-DependencyModel",
                    "area-Extensions-DependencyInjection",
                    "area-System.ComponentModel",
                    "area-System.Drawing",
                    "area-System.Globalization",
            }.ToHashSet();
            sir.Repos.Add(owner, repo);
            sir.Page = 1;
            _logger.LogInformation("sir.Page: " + sir.Page);
            var xx = await client.Search.SearchIssues(sir);
            bool addIssue = false;
            foreach (var item in xx.Items)
            {
                if (skipFor.Contains(item.Number))
                    continue;

                foreach (var label in item.Labels)
                {
                    if (acceptableLabels.Contains(label.Name))
                    {
                        addIssue = true;
                        break;
                    }
                }

                if (addIssue)
                {
                    addIssue = false;
                    skipFor.Add(item.Number);
                    var pr = await client.PullRequest.Get(owner, repo, item.Number);

                    var newCard = new NewProjectCard(pr.Id, ProjectCardContentType.PullRequest);
                    try
                    {
                        var result = await client.Repository.Project.Card.Create(12751161, newCard);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"#{item.Number}: ex was of type {ex.GetType()}, message: {ex.Message}");
                    }
                }
            }
        }

        public async Task MoveFromUncommittedToFuture(string owner, string repo)
        {
            bool disable = true;
            if (disable)
                return;

            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

            var existingCards = await client.Repository.Project.Card.GetAll(9664733); // Uncommitted
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var futureMilestoneIssuesInUncommitted = new HashSet<(Issue, ProjectCard)>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                if (myCard.ContentUrl == null)
                    continue;

                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    var issue = await GetIssue(owner, repo, issueNumber);
                    if (issue.Milestone.Title.Equals("Future"))
                    {
                        futureMilestoneIssuesInUncommitted.Add((issue, myCard));
                    }
                }
            }

            foreach ((Issue issue, ProjectCard card) issueToMove in futureMilestoneIssuesInUncommitted)
            {
                if (issueToMove.issue.Number == 40848)
                {
                    continue; // skip existing
                }
                var projectMove = new ProjectCardMove(ProjectCardPosition.Top, 12751157, cardId: null); // 
                try
                {
                    var result = await client.Repository.Project.Card.Move(issueToMove.card.Id, projectMove);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"#{issueToMove.issue}: ex was of type {ex.GetType()}, message: {ex.Message}");
                }
            }
        }

        public async Task<bool> AddCardToProject(string owner, string repo)
        {
            bool disable = true;
            if (disable)
                return false;
            var client = _gitHubClientFactory.GetUserClient();
            //var projects = await _client.Repository.Project.GetAllForRepository(owner, repo);
            //foreach (var project in projects)
            //{
            //    _logger.LogInformation($"{project.Name}, {project.Id}");
            //}
            var columns = await client.Repository.Project.Column.GetAll(3935839); // ML/Extensions pod: the project we care about
            foreach (var col in columns)
            {
                _logger.LogInformation($"{col.Name}, {col.Id}");
            }

               var existingCards = await client.Repository.Project.Card.GetAll(12751149); // Untriaged
            var regex = new Regex($@"{owner}/{repo}/issues/(\d+)");
            var skipFor = new HashSet<int>();
            foreach (var curCard in existingCards)
            {
                var myCard = await client.Repository.Project.Card.Get(curCard.Id);
                Match match = regex.Match(myCard.ContentUrl);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
                {
                    skipFor.Add(issueNumber);
                }
            }

            // get all untriaged issues in the repo
            // https://github.com/dotnet/corefx/pulls?q=is%3Aopen+is%3Apr+label%3Atenet-performance
            var sir = new SearchIssuesRequest()
            {
                Is = new[] { IssueIsQualifier.Issue },
                Labels = new[] { "needs further triage" },
                State = ItemState.Open, // Closed to get closed ones
            };
            var acceptableLabels = new string[]
            {
                    "area-Extensions-Logging",
                    "area-Extensions-Options",
                    "area-Extensions-Primitives",
                    "area-Extensions-Caching",
                    "area-Extensions-Configuration",
                    "area-Extensions-Hosting",
                    "area-DependencyModel",
                    "area-Extensions-DependencyInjection",
                    "area-System.ComponentModel",
                    "area-System.Drawing",
                    "area-System.Globalization",
            }.ToHashSet();
            sir.Repos.Add(owner, repo);
            sir.Page = 1;
            _logger.LogInformation("sir.Page: " + sir.Page);
            var xx = await client.Search.SearchIssues(sir);
            bool addIssue = false;
            foreach (var item in xx.Items)
            {
                if (skipFor.Contains(item.Number))
                    continue;

                foreach (var label in item.Labels)
                {
                    if (acceptableLabels.Contains(label.Name))
                    {
                        addIssue = true;
                        break;
                    }
                }

                if (addIssue)
                {
                    addIssue = false;
                    skipFor.Add(item.Number);
                    var newCard = new NewProjectCard(item.Id, ProjectCardContentType.Issue);
                    try
                    {
                        var result = await client.Repository.Project.Card.Create(12751149, newCard);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"#{item.Number}: ex was of type {ex.GetType()}, message: {ex.Message}");
                    }
                }
            }
            return default;
        }

        public async Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            Octokit.PullRequest iop = null;
            try
            {
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            IReadOnlyList<PullRequestFile> prFiles = null;
            try
            {
                prFiles = await _client.PullRequest.Files(owner, repo, number);

            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                prFiles = await _client.PullRequest.Files(owner, repo, number);
            }
            return prFiles;
        }
    }
}