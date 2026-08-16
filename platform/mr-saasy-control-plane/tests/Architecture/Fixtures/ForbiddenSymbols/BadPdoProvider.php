<?php

namespace Tests\Architecture\Fixtures\ForbiddenSymbols;

use PDO;

final class BadPdoProvider
{
    public function connect(): PDO
    {
        return new PDO('sqlsrv:Server=forbidden;Database=forbidden');
    }
}
