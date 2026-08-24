<?php

use App\AI\Application\Observability\AgentGraphProjector;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;

Route::get('/', static fn () => response()->json([
    'service' => 'mr-saasy-control-plane',
    'state' => 'gate-0',
    'directDbAccess' => false,
    'agentGraph' => '/agent-graph',
]));

Route::get('/agent-graph', static fn () => response()->file(public_path('agent-graph.html')));

Route::get('/api/agent-graph', static function (AgentGraphProjector $projector) {
    $events = config('agent-graph.events', []);

    return response()->json([
        'mode' => config('agent-graph.mode', 'wiring-prototype'),
        'snapshot' => $projector->project(is_array($events) ? $events : [])->toArray(),
        'executionEnabled' => false,
    ]);
});

Route::get('/api/agent-graph/preview-delegation', static function (Request $request, AgentGraphProjector $projector) {
    $validated = $request->validate([
        'taskId' => ['required', 'string', 'max:100'],
        'to' => ['required', 'string', 'max:100'],
        'from' => ['nullable', 'string', 'max:100'],
    ]);

    $events = config('agent-graph.events', []);
    $events = is_array($events) ? $events : [];

    $snapshot = $projector->project($events);
    $nodeIds = array_column($snapshot->nodes, 'id');
    $taskIds = array_column($snapshot->tasks, 'id');

    if (!in_array($validated['to'], $nodeIds, true) || !in_array($validated['taskId'], $taskIds, true)) {
        return response()->json(['message' => 'Unknown task or agent. Delegation fails closed.'], 422);
    }

    $target = collect($snapshot->nodes)->firstWhere('id', $validated['to']);
    if (!is_array($target) || !in_array($target['kind'] ?? null, ['agent', 'orchestrator', 'reviewer'], true)) {
        return response()->json(['message' => 'Tasks can only be delegated to an agent-capable node.'], 422);
    }

    $events[] = [
        'type' => 'TaskDelegated',
        'at' => now()->toIso8601String(),
        'summary' => sprintf('%s delegated to %s (preview)', $validated['taskId'], $validated['to']),
        'payload' => [
            'taskId' => $validated['taskId'],
            'from' => $validated['from'] ?? 'pi',
            'to' => $validated['to'],
            'preview' => true,
        ],
    ];

    return response()->json([
        'mode' => 'preview',
        'snapshot' => $projector->project($events)->toArray(),
        'executionEnabled' => false,
        'message' => 'Preview only. Authenticated command execution is not enabled in Gate 0.',
    ]);
});
