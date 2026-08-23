<?php

namespace App\AI\Application\Routing;

enum ToolCapability: string
{
    case RepositoryRead = 'repository_read';
    case PullRequestRead = 'pull_request_read';
    case WebSearch = 'web_search';
    case SocialSearch = 'social_search';
    case Browser = 'browser';
    case DocumentationWrite = 'documentation_write';
}
