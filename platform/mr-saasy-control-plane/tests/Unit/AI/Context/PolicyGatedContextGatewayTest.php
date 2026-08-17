<?php

namespace Tests\Unit\AI\Context;

use App\AI\Context\AgentContextRequest;
use App\AI\Context\CapabilityContextPolicy;
use App\AI\Context\ContextAccessDenied;
use App\AI\Context\ContextDenialReason;
use App\AI\Context\PolicyGatedContextGateway;
use App\Platform\Audit\AuditSink;
use App\Platform\Contracts\TenantContext;
use App\Platform\Policy\ContextPolicy;
use App\ProductAdapters\Contracts\ProductContextPort;
use PHPUnit\Framework\TestCase;

final class PolicyGatedContextGatewayTest extends TestCase
{
    private const string CAPABILITY = 'customer_support_summary';

    /** Payload values that must never appear in returned-but-unscoped data or in audit evidence. */
    private const array SENSITIVE_VALUES = ['Ada Lovelace', 'ada@example.com', 'enterprise', 'do not expose'];

    public function test_allowed_request_returns_only_permitted_requested_fields(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        $gateway = $this->gateway(new ConfiguredContextPolicy([self::CAPABILITY]), $port, new RecordingAuditSink());

        $context = $gateway->load($this->request(['display_name', 'plan', 'internal_notes']));

        self::assertSame(['display_name' => 'Ada Lovelace', 'plan' => 'enterprise'], $context->values);
        // Minimization happens before the port: it is never asked for the ungranted field.
        self::assertSame([['display_name', 'plan']], $port->requestedFieldSets);
        self::assertSame(1, $port->callCount);
    }

    public function test_forbidden_capability_denies_with_zero_payload(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        $audit = new RecordingAuditSink();
        $gateway = $this->gateway(new ConfiguredContextPolicy([]), $port, $audit);

        $denied = $this->denial(fn () => $gateway->load($this->request(['display_name'])));

        self::assertSame(ContextDenialReason::CapabilityForbidden, $denied->reason);
        self::assertSame(0, $port->callCount);
        self::assertSame('capability_forbidden', $this->lastDecision($audit)['reason']);
    }

    public function test_unknown_capability_denies_even_when_authorization_would_allow_it(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        // Authorization would say yes, but the capability is not declared in the context policy.
        $gateway = $this->gateway(new ConfiguredContextPolicy(['mystery_capability']), $port, new RecordingAuditSink());

        $denied = $this->denial(fn () => $gateway->load($this->request(['display_name'], capability: 'mystery_capability')));

        self::assertSame(ContextDenialReason::UnknownCapability, $denied->reason);
        self::assertSame(0, $port->callCount);
    }

    public function test_cross_tenant_request_denies_before_loading(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        $gateway = $this->gateway(new ConfiguredContextPolicy([self::CAPABILITY]), $port, new RecordingAuditSink());

        $denied = $this->denial(fn () => $gateway->load(
            $this->request(['display_name'], acting: 'tenant-a', target: 'tenant-b'),
        ));

        self::assertSame(ContextDenialReason::CrossTenantAccess, $denied->reason);
        self::assertSame(0, $port->callCount);
    }

    public function test_masked_fields_are_masked_in_returned_context(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        $gateway = $this->gateway(new ConfiguredContextPolicy([self::CAPABILITY]), $port, new RecordingAuditSink());

        $context = $gateway->load($this->request(['display_name', 'email']));

        self::assertSame('Ada Lovelace', $context->values['display_name']);
        self::assertSame('***masked***', $context->values['email']);
    }

    public function test_request_with_no_permitted_fields_fails_closed(): void
    {
        $port = new RecordingProductContextPort($this->fixtureData());
        $gateway = $this->gateway(new ConfiguredContextPolicy([self::CAPABILITY]), $port, new RecordingAuditSink());

        $denied = $this->denial(fn () => $gateway->load($this->request(['internal_notes'])));

        self::assertSame(ContextDenialReason::NoPermittedFields, $denied->reason);
        self::assertSame(0, $port->callCount);
    }

    public function test_every_decision_is_auditable_without_payload_values(): void
    {
        $audit = new RecordingAuditSink();
        $gateway = $this->gateway(
            new ConfiguredContextPolicy([self::CAPABILITY]),
            new RecordingProductContextPort($this->fixtureData()),
            $audit,
        );

        $gateway->load($this->request(['display_name', 'email']));

        $events = array_column($audit->events, 'event');
        self::assertContains('context.request', $events);
        self::assertContains('context.decision', $events);

        $decision = $this->lastDecision($audit);
        self::assertSame(self::CAPABILITY, $decision['capability']);
        self::assertSame('ALLOWED', $decision['decision']);
        self::assertNull($decision['reason']);
        self::assertSame('display_name,email', $decision['granted_fields']);
        self::assertSame('email', $decision['masked_fields']);

        foreach ($audit->events as $event) {
            foreach ($event['metadata'] as $value) {
                $flat = $value === null ? '' : (string) $value;
                foreach (self::SENSITIVE_VALUES as $secret) {
                    self::assertStringNotContainsString($secret, $flat, "Audit leaked payload value into '{$event['event']}'.");
                }
            }
        }
    }

    private function gateway(ContextPolicy $authorization, ProductContextPort $port, AuditSink $audit): PolicyGatedContextGateway
    {
        return new PolicyGatedContextGateway($authorization, $this->capabilityPolicy(), $port, $audit);
    }

    private function capabilityPolicy(): CapabilityContextPolicy
    {
        return new CapabilityContextPolicy([
            self::CAPABILITY => [
                'fields' => ['display_name', 'plan', 'email', 'ticket_history'],
                'masked' => ['email'],
            ],
        ]);
    }

    /** @return array<string, scalar|array|null> */
    private function fixtureData(): array
    {
        return [
            'display_name' => 'Ada Lovelace',
            'plan' => 'enterprise',
            'email' => 'ada@example.com',
            'ticket_history' => ['t-1', 't-2'],
            'internal_notes' => 'do not expose',
        ];
    }

    /** @param list<string> $fields */
    private function request(
        array $fields,
        string $capability = self::CAPABILITY,
        string $acting = 'tenant-a',
        string $target = 'tenant-a',
    ): AgentContextRequest {
        return new AgentContextRequest(
            new TenantContext($acting),
            new TenantContext($target),
            $capability,
            $fields,
            'agent-support-1',
            'answer support ticket',
            'workslip',
            'production',
        );
    }

    private function denial(callable $act): ContextAccessDenied
    {
        try {
            $act();
        } catch (ContextAccessDenied $denied) {
            return $denied;
        }

        self::fail('Expected ContextAccessDenied to be thrown.');
    }

    /** @return array<string, scalar|null> */
    private function lastDecision(RecordingAuditSink $audit): array
    {
        $decisions = array_values(array_filter(
            $audit->events,
            static fn (array $event): bool => $event['event'] === 'context.decision',
        ));

        self::assertNotSame([], $decisions, 'Expected a context.decision audit event.');

        return $decisions[count($decisions) - 1]['metadata'];
    }
}

final class RecordingAuditSink implements AuditSink
{
    /** @var list<array{event: string, metadata: array<string, scalar|null>}> */
    public array $events = [];

    public function record(string $event, array $metadata): void
    {
        $this->events[] = ['event' => $event, 'metadata' => $metadata];
    }
}

final class ConfiguredContextPolicy implements ContextPolicy
{
    /** @param list<string> $allowedCapabilities */
    public function __construct(private array $allowedCapabilities)
    {
    }

    public function allows(TenantContext $tenant, string $capability): bool
    {
        return in_array($capability, $this->allowedCapabilities, true);
    }
}

final class RecordingProductContextPort implements ProductContextPort
{
    public int $callCount = 0;

    /** @var list<list<string>> */
    public array $requestedFieldSets = [];

    /** @param array<string, scalar|array|null> $data */
    public function __construct(private array $data)
    {
    }

    public function loadSanitizedContext(TenantContext $tenant, array $fields): array
    {
        $this->callCount++;
        $this->requestedFieldSets[] = array_values($fields);

        $result = [];
        foreach ($fields as $field) {
            if (array_key_exists($field, $this->data)) {
                $result[$field] = $this->data[$field];
            }
        }

        return $result;
    }
}
