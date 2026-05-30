using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

public class DynamicPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;
    private readonly IConfiguration _configuration;

    public DynamicPolicyProvider(IOptions<AuthorizationOptions> options, IConfiguration configuration)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
        _configuration = configuration;
    }

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var requiredRole = _configuration[$"Authorization:Policies:{policyName}"];

        if (!string.IsNullOrEmpty(requiredRole))
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new DynamicRoleRequirement(requiredRole));
            var builtPolicy = policy.Build();
            return builtPolicy;
        }


        return await _fallbackProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();
}