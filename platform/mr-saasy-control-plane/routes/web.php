<?php

use Illuminate\Support\Facades\Route;

Route::get('/', static fn () => response()->json([
    'service' => 'mr-saasy-control-plane',
    'state' => 'gate-0',
    'directDbAccess' => false,
]));
