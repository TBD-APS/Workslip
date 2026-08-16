<?php

namespace App\AI\Application\Routing;

enum Capability: string
{
    case Reasoning = 'reasoning';
    case Coding = 'coding';
    case LargeContext = 'large_context';
    case StructuredOutput = 'structured_output';
    case ToolCalling = 'tool_calling';
    case SecurityAnalysis = 'security_analysis';
    case VisualUnderstanding = 'visual_understanding';
}
