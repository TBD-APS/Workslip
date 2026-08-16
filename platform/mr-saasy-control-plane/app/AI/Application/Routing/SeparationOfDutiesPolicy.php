<?php

namespace App\AI\Application\Routing;

final class SeparationOfDutiesPolicy
{
    public static function canBeSoleApprovingReview(
        RunProvenance $implementation,
        RunProvenance $review,
    ): bool {
        if ($implementation->agentId === $review->agentId) {
            return false;
        }

        return !(
            $implementation->provider === $review->provider
            && $implementation->model === $review->model
        );
    }
}
