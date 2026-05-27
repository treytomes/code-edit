using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

public class SearchService(ILogger<SearchService> logger)
{
    // Find and replace logic will be implemented per the search/replace feature spec.
    private readonly ILogger<SearchService> _logger = logger;
}
