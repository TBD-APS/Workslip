from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace(path: str, old: str, new: str) -> None:
    file = ROOT / path
    text = file.read_text(encoding="utf-8-sig")
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"Expected source not found in {path}: {old[:160]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace(
    "src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs",
    """        var results = new List<InviteUserResult>();
        foreach (var email in request.Emails)
        {
            var result = await ProcessInviteEmailAsync(email, organizationId.Value, request.Role, cancellationToken);
            results.Add(result);
        }
""",
    """        var role = NormalizeInviteRole(request.Role);
        if (role is null)
        {
            return Result<InviteUsersResponse>.Invalid(new ValidationError
            {
                Identifier = nameof(InviteUsersRequest.Role),
                ErrorMessage = \"Rollen skal være User eller Auditor.\"
            });
        }

        var results = new List<InviteUserResult>();
        foreach (var email in request.Emails)
        {
            var result = await ProcessInviteEmailAsync(email, organizationId.Value, role, cancellationToken);
            results.Add(result);
        }
""",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs",
    """        string? role,
        CancellationToken cancellationToken)
""",
    """        string role,
        CancellationToken cancellationToken)
""",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs",
    """                existingInvite.Token = token;
                existingInvite.Consumed = false;
                await inviteRepository.UpdateAsync(existingInvite, cancellationToken);
""",
    """                existingInvite.Token = token;
                existingInvite.Role = role;
                existingInvite.Consumed = false;
                await inviteRepository.UpdateAsync(existingInvite, cancellationToken);
""",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs",
    """            logger.LogInformation(\"Invite sent. InviteId: {InviteId}. OrganizationId: {OrganizationId}\", inviteId, organizationId);
""",
    """            logger.LogInformation(
                \"Invite sent. InviteId: {InviteId}. OrganizationId: {OrganizationId}. Role: {Role}.\",
                inviteId,
                organizationId,
                role);
""",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs",
    """    private static UserDataRow BuildUserFromInvite(InviteTokenRow invite, string displayName, string? phone) =>
""",
    """    private static string? NormalizeInviteRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Roles.User;
        }

        var normalized = role.Trim();
        if (normalized.Equals(Roles.User, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.User;
        }

        if (normalized.Equals(Roles.Auditor, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Auditor;
        }

        return null;
    }

    private static UserDataRow BuildUserFromInvite(InviteTokenRow invite, string displayName, string? phone) =>
""",
)

replace(
    "src/FE/src/features/settings/routes/Settings.tsx",
    """import { useDeleteApiAuthInvite, useGetApiAuthInvites, type InviteTokenResponse } from '../api';

export const Settings = () => {
""",
    """import { useDeleteApiAuthInvite, useGetApiAuthInvites, type InviteTokenResponse } from '../api';

type InviteRole = 'User' | 'Auditor';

const getInviteRoleLabel = (role: string | null) => role === 'Auditor' ? 'Revisor' : 'Medarbejder';

export const Settings = () => {
""",
)

replace(
    "src/FE/src/features/settings/routes/Settings.tsx",
    """  const [emails, setEmails] = useState<string[]>([]);
  const [clearingInviteId, setClearingInviteId] = useState<string | null>(null);
""",
    """  const [emails, setEmails] = useState<string[]>([]);
  const [inviteRole, setInviteRole] = useState<InviteRole>('User');
  const [clearingInviteId, setClearingInviteId] = useState<string | null>(null);
""",
)

replace(
    "src/FE/src/features/settings/routes/Settings.tsx",
    """          role: null,
""",
    """          role: inviteRole,
""",
)

replace(
    "src/FE/src/features/settings/routes/Settings.tsx",
    """        <div className=\"invite-input-row\">
""",
    """        <div className=\"form-group invite-role-field\">
          <label className=\"form-label\" htmlFor=\"invite-role\">
            Rolle for invitationerne
          </label>
          <select
            id=\"invite-role\"
            className=\"form-input\"
            value={inviteRole}
            onChange={(event) => setInviteRole(event.target.value as InviteRole)}
            disabled={inviteMutation.isPending}
          >
            <option value=\"User\">Medarbejder (User)</option>
            <option value=\"Auditor\">Revisor (Auditor)</option>
          </select>
          <p className=\"form-help-text\">
            Alle e-mailadresser i denne invitation får den valgte rolle.
          </p>
        </div>

        <div className=\"invite-input-row\">
""",
)

replace(
    "src/FE/src/features/settings/routes/Settings.tsx",
    """                    <span className={`invite-status-badge ${st.cls}`}>
                      <Icon size={12} />
                      {st.label}
                    </span>
""",
    """                    <span className={`invite-status-badge ${st.cls}`}>
                      <Icon size={12} />
                      {st.label}
                    </span>
                    <span className=\"invite-role-badge\">{getInviteRoleLabel(invite.role)}</span>
""",
)

replace(
    "src/FE/src/App.css",
    """.invite-input-row {
  display: flex;
  gap: 0.5rem;
}
""",
    """.invite-role-field {
  margin-bottom: 1rem;
}

.invite-role-field .form-help-text {
  margin: 0.4rem 0 0;
  color: var(--text-muted);
  font-size: var(--fs-xs);
}

.invite-input-row {
  display: flex;
  gap: 0.5rem;
}
""",
)

replace(
    "src/FE/src/App.css",
    """.invite-status-badge {
""",
    """.invite-role-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.15rem 0.45rem;
  border-radius: var(--radius-pill);
  background: var(--surface-raised);
  color: var(--text-muted);
  font-size: var(--fs-xs);
  white-space: nowrap;
}

.invite-status-badge {
""",
)

replace(
    "Docs/api/contract.md",
    """Admin-authorized invitation status operations are:

```text
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```

The delete operation is tenant-scoped by the authenticated organization.
""",
    """Admin-authorized invitation operations are:

```text
POST   /api/auth/invite
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```

`POST /api/auth/invite` accepts one or more e-mail addresses and an invitation role. The only assignable roles are canonical `User` and `Auditor`; missing or blank roles retain the backward-compatible `User` default. Any other value, including `Admin` and `Superadmin`, is rejected before an invitation or e-mail side effect occurs. Resending a pending invitation replaces its role with the latest valid selection.

The delete operation is tenant-scoped by the authenticated organization.
""",
)

backend_test = r'''using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Invitations;

public sealed class InvitationRoleTests
{
    [Fact]
    public async Task InviteUsersAsync_PersistsAuditorRole()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["auditor@example.com"], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Roles.Auditor, Assert.Single(repository.Created).Role);
        Assert.Equal("auditor@example.com", Assert.Single(emailService.InviteRecipients));
    }

    [Fact]
    public async Task InviteUsersAsync_UpdatesRoleWhenPendingInviteIsResent()
    {
        var organizationId = Guid.NewGuid();
        var existing = CreateInvite(organizationId, Roles.User);
        var repository = new RecordingInviteRepository(existing);
        var service = CreateService(repository, new RecordingEmailService(), organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest([existing.Email], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(existing, Assert.Single(repository.Updated));
        Assert.Equal(Roles.Auditor, existing.Role);
        Assert.False(existing.Consumed);
    }

    [Fact]
    public async Task InviteUsersAsync_DefaultsMissingRoleToUser()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var service = CreateService(repository, new RecordingEmailService(), organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["user@example.com"], "https://app.example", Role: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Roles.User, Assert.Single(repository.Created).Role);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Superadmin")]
    [InlineData("Owner")]
    public async Task InviteUsersAsync_RejectsRolesOutsideUserAndAuditor(string role)
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["privilege@example.com"], "https://app.example", role),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(repository.Created);
        Assert.Empty(repository.Updated);
        Assert.Empty(emailService.InviteRecipients);
        Assert.Contains(result.ValidationErrors, error => error.Identifier == nameof(InviteUsersRequest.Role));
    }

    private static InvitationService CreateService(
        IInviteRepository inviteRepository,
        IEmailService emailService,
        Guid organizationId) =>
        new(
            null!,
            inviteRepository,
            null!,
            null!,
            emailService,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin),
            NullLogger<InvitationService>.Instance);

    private static InviteTokenRow CreateInvite(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "pending@example.com",
        Token = Guid.NewGuid().ToString("N"),
        Role = role,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        Consumed = false
    };

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingInviteRepository(InviteTokenRow? existing = null) : IInviteRepository
    {
        public List<InviteTokenRow> Created { get; } = [];
        public List<InviteTokenRow> Updated { get; } = [];

        public Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
        {
            Created.Add(invite);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
        {
            Updated.Add(invite);
            return Task.CompletedTask;
        }

        public Task<InviteTokenRow?> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
            Task.FromResult(existing is not null
                && existing.OrganizationId == organizationId
                && string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase)
                    ? existing
                    : null);

        public Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public List<string> InviteRecipients { get; } = [];

        public Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken)
        {
            InviteRecipients.Add(toEmail);
            return Task.CompletedTask;
        }

        public Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
'''

backend_test_path = ROOT / "src/BE/WorkslipApi/Workslip.Tests/Invitations/InvitationRoleTests.cs"
backend_test_path.parent.mkdir(parents=True, exist_ok=True)
if not backend_test_path.exists():
    backend_test_path.write_text(backend_test, encoding="utf-8")

frontend_test = r'''import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Settings } from './Settings';

const { inviteMutation, invalidateQueries } = vi.hoisted(() => ({
  inviteMutation: vi.fn(),
  invalidateQueries: vi.fn(),
}));

vi.mock('@tanstack/react-query', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-query')>('@tanstack/react-query');
  return {
    ...actual,
    useQueryClient: () => ({ invalidateQueries }),
  };
});

vi.mock('../../../api/generated/auth/auth', () => ({
  usePostApiAuthInvite: () => ({
    mutateAsync: inviteMutation,
    isPending: false,
  }),
}));

vi.mock('../api', () => ({
  useGetApiAuthInvites: () => ({
    isLoading: false,
    isError: false,
    data: { invites: [] },
  }),
  useDeleteApiAuthInvite: () => ({
    mutateAsync: vi.fn(),
    isPending: false,
  }),
}));

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

afterEach(() => {
  cleanup();
  inviteMutation.mockReset();
  inviteMutation.mockResolvedValue({ results: [] });
  invalidateQueries.mockReset();
});

describe('Settings invitation role', () => {
  it('defaults to User and sends the selected Auditor role', async () => {
    render(
      <MemoryRouter>
        <Settings />
      </MemoryRouter>,
    );

    const roleSelect = screen.getByRole('combobox', { name: 'Rolle for invitationerne' });
    expect(roleSelect).toHaveValue('User');

    fireEvent.change(roleSelect, { target: { value: 'Auditor' } });
    fireEvent.change(screen.getByPlaceholderText('Skriv e-mail...'), {
      target: { value: 'auditor@example.com' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj e-mail' }));
    fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => {
      expect(inviteMutation).toHaveBeenCalledWith({
        data: {
          emails: ['auditor@example.com'],
          role: 'Auditor',
          inviteBaseUrl: window.location.origin,
        },
      });
    });
  });
});
'''

frontend_test_path = ROOT / "src/FE/src/features/settings/routes/Settings.invitation-role.test.tsx"
if not frontend_test_path.exists():
    frontend_test_path.write_text(frontend_test, encoding="utf-8")

playwright_spec = r'''import { expect, test, type Page } from '@playwright/test';

const baseUrl = 'http://127.0.0.1:4173';

function attachDiagnostics(page: Page) {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

test.use({ viewport: { width: 390, height: 844 } });

test('admin selects Auditor and sends that canonical role', async ({ page }) => {
  const browserErrors = attachDiagnostics(page);
  const failedRequests: string[] = [];
  let submittedBody: Record<string, unknown> | null = null;

  page.on('requestfailed', (request) => {
    failedRequests.push(`${request.method()} ${request.url()}`);
  });

  await page.addInitScript(() => {
    localStorage.setItem('authToken', 'playwright-admin-token');
    localStorage.setItem('userEmail', 'admin@example.com');
  });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;

    if (pathname === '/api/auth/me') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: '11111111-1111-1111-1111-111111111111',
          organizationId: '22222222-2222-2222-2222-222222222222',
          email: 'admin@example.com',
          displayName: 'Testadministrator',
          phone: '',
          role: 'Admin',
          roleDisplayName: 'Administrator',
          hoursThisWeek: null,
          hoursThisMonth: null,
          hoursBiweekly: null,
        }),
      });
      return;
    }

    if (pathname === '/api/auth/invites' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ invites: [] }),
      });
      return;
    }

    if (pathname === '/api/auth/invite' && request.method() === 'POST') {
      submittedBody = request.postDataJSON() as Record<string, unknown>;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          results: [{ email: 'auditor@example.com', success: true, error: null, inviteLink: null }],
        }),
      });
      return;
    }

    if (pathname === '/api/push-subscriptions/public-key') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
  });

  await page.goto(`${baseUrl}/app/settings`, { waitUntil: 'domcontentloaded' });

  await expect(page.getByRole('heading', { name: 'Administrativt' })).toBeVisible();
  const roleSelect = page.getByRole('combobox', { name: 'Rolle for invitationerne' });
  await expect(roleSelect).toHaveValue('User');
  await roleSelect.selectOption('Auditor');

  await page.getByPlaceholder('Skriv e-mail...').fill('auditor@example.com');
  await page.getByRole('button', { name: 'Tilføj e-mail' }).click();
  await page.getByRole('button', { name: 'Send invitation' }).click();

  await expect.poll(() => submittedBody).not.toBeNull();
  expect(submittedBody).toMatchObject({
    emails: ['auditor@example.com'],
    role: 'Auditor',
  });
  await expect(page.getByText('1 invitation(er) sendt')).toBeVisible();
  expect(browserErrors).toEqual([]);
  expect(failedRequests).toEqual([]);
});
'''

(ROOT / "src/FE/wor-244.validation.spec.ts").write_text(playwright_spec, encoding="utf-8")
