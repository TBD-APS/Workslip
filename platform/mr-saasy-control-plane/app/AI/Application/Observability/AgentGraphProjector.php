<?php

namespace App\AI\Application\Observability;

use App\Platform\Contracts\AgentGraphSnapshot;

final class AgentGraphProjector
{
    /**
     * @param list<array<string, mixed>> $events
     */
    public function project(array $events): AgentGraphSnapshot
    {
        $nodes = [];
        $edges = [];
        $tasks = [];
        $activity = [];

        foreach ($events as $event) {
            $type = (string) ($event['type'] ?? 'Unknown');
            $at = (string) ($event['at'] ?? '');
            $payload = is_array($event['payload'] ?? null) ? $event['payload'] : [];

            if ($type === 'NodeObserved') {
                $id = (string) ($payload['id'] ?? '');
                if ($id !== '') {
                    $nodes[$id] = [
                        'id' => $id,
                        'label' => (string) ($payload['label'] ?? $id),
                        'kind' => (string) ($payload['kind'] ?? 'agent'),
                        'status' => (string) ($payload['status'] ?? 'planned'),
                        'detail' => (string) ($payload['detail'] ?? ''),
                    ];
                }
            }

            if ($type === 'TaskObserved') {
                $id = (string) ($payload['id'] ?? '');
                if ($id !== '') {
                    $tasks[$id] = [
                        'id' => $id,
                        'title' => (string) ($payload['title'] ?? $id),
                        'owner' => $payload['owner'] ?? null,
                        'status' => (string) ($payload['status'] ?? 'planned'),
                    ];
                }
            }

            if ($type === 'TaskDelegated') {
                $taskId = (string) ($payload['taskId'] ?? '');
                $to = (string) ($payload['to'] ?? '');
                $from = (string) ($payload['from'] ?? '');
                if ($taskId !== '') {
                    $tasks[$taskId] = array_merge($tasks[$taskId] ?? ['id' => $taskId, 'title' => $taskId], [
                        'owner' => $to,
                        'status' => 'running',
                    ]);
                }
                if ($from !== '' && $to !== '') {
                    $edges['delegation:'.$taskId] = [
                        'id' => 'delegation:'.$taskId,
                        'from' => $from,
                        'to' => $to,
                        'kind' => 'delegated_to',
                        'label' => $taskId,
                        'status' => 'active',
                    ];
                }
            }

            if ($type === 'DependencyObserved') {
                $from = (string) ($payload['from'] ?? '');
                $to = (string) ($payload['to'] ?? '');
                $kind = (string) ($payload['kind'] ?? 'depends_on');
                if ($from !== '' && $to !== '') {
                    $id = $kind.':'.$from.':'.$to;
                    $edges[$id] = [
                        'id' => $id,
                        'from' => $from,
                        'to' => $to,
                        'kind' => $kind,
                        'label' => (string) ($payload['label'] ?? str_replace('_', ' ', $kind)),
                        'status' => (string) ($payload['status'] ?? 'active'),
                    ];
                }
            }

            $activity[] = [
                'type' => $type,
                'at' => $at,
                'summary' => (string) ($event['summary'] ?? $type),
                'payload' => $payload,
            ];
        }

        return new AgentGraphSnapshot(
            array_values($nodes),
            array_values($edges),
            array_values($tasks),
            array_slice(array_reverse($activity), 0, 30),
        );
    }
}
