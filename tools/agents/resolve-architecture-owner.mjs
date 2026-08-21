#!/usr/bin/env node
import process from 'node:process';
import { loadArchitectureOwners, resolveArchitectureOwner } from './architecture-owners.mjs';

const intent = process.argv[2];
if (!intent) {
  console.error('Usage: node tools/agents/resolve-architecture-owner.mjs <intent>');
  process.exit(2);
}

const registry = await loadArchitectureOwners();
const owner = resolveArchitectureOwner(registry, intent);

if (!owner) {
  console.error(`No architecture owner registered for intent '${intent}'.`);
  process.exit(3);
}

console.log(JSON.stringify(owner, null, 2));
