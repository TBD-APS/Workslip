<?php

namespace Tests\Architecture\Fixtures\ForbiddenSymbols;

use PDO as HiddenDatabaseClient;

final class BadAliasedPdoProvider
{
    public function connect(): HiddenDatabaseClient
    {
        return new HiddenDatabaseClient('sqlsrv:Server=forbidden;Database=forbidden');
    }
}
