<?php

namespace Tests\Unit\AI\Routing;

use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\RunProvenance;
use DateTimeImmutable;
use InvalidArgumentException;
use PHPUnit\Framework\TestCase;

final class RunProvenanceTest extends TestCase
{
    public function test_rejects_completion_before_start(): void
    {
        $this->expectException(InvalidArgumentException::class);
        $this->expectExceptionMessage('Run completion cannot precede run start.');

        $this->provenance(
            new DateTimeImmutable('2026-08-16T08:00:00+00:00'),
            new DateTimeImmutable('2026-08-16T07:59:59+00:00'),
        );
    }

    public function test_accepts_running_run_without_completion(): void
    {
        $run = $this->provenance(
            new DateTimeImmutable('2026-08-16T08:00:00+00:00'),
            null,
        );

        self::assertNull($run->completedAt);
    }

    public function test_accepts_equal_or_later_completion_and_earlier_research_observation(): void
    {
        $startedAt = new DateTimeImmutable('2026-08-16T08:00:00+00:00');
        $researchObservedAt = new DateTimeImmutable('2026-08-15T12:00:00+00:00');
        $laterCompletedAt = $startedAt->modify('+1 minute');

        $equal = $this->provenance($startedAt, $startedAt, $researchObservedAt);
        $later = $this->provenance($startedAt, $laterCompletedAt, $researchObservedAt);

        self::assertSame($startedAt, $equal->completedAt);
        self::assertSame($laterCompletedAt, $later->completedAt);
        self::assertSame($researchObservedAt, $later->researchObservedAt);
    }

    private function provenance(
        DateTimeImmutable $startedAt,
        ?DateTimeImmutable $completedAt,
        ?DateTimeImmutable $researchObservedAt = null,
    ): RunProvenance {
        return new RunProvenance(
            'run-1',
            AgentRole::ImplementationStandard,
            'agent-1',
            'provider-a',
            'model-a',
            $startedAt,
            $completedAt,
            ['evidence://run-1'],
            $researchObservedAt,
        );
    }
}
