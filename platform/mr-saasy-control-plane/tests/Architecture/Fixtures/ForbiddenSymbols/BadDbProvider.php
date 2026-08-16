<?php

namespace Tests\Architecture\Fixtures\ForbiddenSymbols;

use Illuminate\Support\Facades\DB;

final class BadDbProvider
{
    public function query(): mixed
    {
        return DB::table('forbidden')->first();
    }
}
