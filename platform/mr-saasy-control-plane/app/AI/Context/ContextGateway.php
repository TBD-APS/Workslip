<?php

namespace App\AI\Context;

interface ContextGateway
{
    /**
     * Resolve the minimized, masked product context an agent is permitted to see.
     *
     * A denied request MUST fail closed: implementations throw
     * {@see ContextAccessDenied} before any customer payload is loaded and never
     * return partial or unscoped data.
     */
    public function load(AgentContextRequest $request): SanitizedContext;
}
