#!/usr/bin/env node
import process from 'node:process';
import { loadArchitectureOwners, validateArchitectureOwners } from './architecture-owners.mjs';

const registry = await loadArchitectureOwners();
const errors = await validateArchitectureOwners(registry);

if (errors.length > 0) {
  console.error('Architecture owner registry validation failed:');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`Architecture owner registry passed (${Object.keys(registry.owners).length} owners).`);
