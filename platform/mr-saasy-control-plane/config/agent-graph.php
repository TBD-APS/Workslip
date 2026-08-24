<?php

return [
    'mode' => 'wiring-prototype',
    'events' => [
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:00+02:00', 'summary' => 'Pi orchestrator online', 'payload' => ['id' => 'pi', 'label' => 'Pi', 'kind' => 'orchestrator', 'status' => 'running', 'detail' => 'Plans, delegates and owns integration']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:01+02:00', 'summary' => 'Kimi frontend specialist available', 'payload' => ['id' => 'kimi', 'label' => 'Kimi', 'kind' => 'agent', 'status' => 'waiting', 'detail' => 'Frontend / UX implementation and review']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:02+02:00', 'summary' => 'Backend specialist active', 'payload' => ['id' => 'backend', 'label' => 'Backend', 'kind' => 'agent', 'status' => 'running', 'detail' => 'API, contracts and domain invariants']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:03+02:00', 'summary' => 'Reviewer available', 'payload' => ['id' => 'reviewer', 'label' => 'Reviewer', 'kind' => 'reviewer', 'status' => 'waiting', 'detail' => 'Independent review / attack assumptions']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:04+02:00', 'summary' => 'GitHub connected', 'payload' => ['id' => 'github', 'label' => 'GitHub', 'kind' => 'system', 'status' => 'running', 'detail' => 'Branches, PRs, commits and checks']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:05+02:00', 'summary' => 'Linear connected', 'payload' => ['id' => 'linear', 'label' => 'Linear', 'kind' => 'system', 'status' => 'running', 'detail' => 'Tasks and delivery state']],
        ['type' => 'NodeObserved', 'at' => '2026-08-24T00:00:06+02:00', 'summary' => 'CI gate connected', 'payload' => ['id' => 'ci', 'label' => 'CI Gate', 'kind' => 'gate', 'status' => 'waiting', 'detail' => 'Deterministic and browser evidence gates']],
        ['type' => 'TaskObserved', 'at' => '2026-08-24T00:01:00+02:00', 'summary' => 'Agent Graph task created', 'payload' => ['id' => 'SASSY-GRAPH-01', 'title' => 'Agent Graph control room', 'owner' => 'pi', 'status' => 'running']],
        ['type' => 'TaskObserved', 'at' => '2026-08-24T00:01:10+02:00', 'summary' => 'Frontend graph task ready', 'payload' => ['id' => 'SASSY-GRAPH-UI', 'title' => 'Interactive graph + drag delegation', 'owner' => null, 'status' => 'planned']],
        ['type' => 'TaskObserved', 'at' => '2026-08-24T00:01:20+02:00', 'summary' => 'Review task ready', 'payload' => ['id' => 'SASSY-GRAPH-REVIEW', 'title' => 'Independent graph review', 'owner' => null, 'status' => 'planned']],
        ['type' => 'DependencyObserved', 'at' => '2026-08-24T00:02:00+02:00', 'summary' => 'Pi writes delivery state to Linear', 'payload' => ['from' => 'pi', 'to' => 'linear', 'kind' => 'produces', 'label' => 'task state', 'status' => 'active']],
        ['type' => 'DependencyObserved', 'at' => '2026-08-24T00:02:10+02:00', 'summary' => 'Pi coordinates work through GitHub', 'payload' => ['from' => 'pi', 'to' => 'github', 'kind' => 'produces', 'label' => 'branch / PR', 'status' => 'active']],
        ['type' => 'DependencyObserved', 'at' => '2026-08-24T00:02:20+02:00', 'summary' => 'GitHub feeds CI', 'payload' => ['from' => 'github', 'to' => 'ci', 'kind' => 'triggers', 'label' => 'exact revision', 'status' => 'active']],
        ['type' => 'DependencyObserved', 'at' => '2026-08-24T00:02:30+02:00', 'summary' => 'Reviewer gates merge', 'payload' => ['from' => 'reviewer', 'to' => 'ci', 'kind' => 'reviews', 'label' => 'review evidence', 'status' => 'waiting']],
    ],
];
