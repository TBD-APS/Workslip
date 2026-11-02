import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { validateProfile } from './profile.mjs';

const quote = value => `'${value.replaceAll("'", "'\\''")}'`;

export async function scaffoldDelivery({ directory, profile: input, validation }) {
  const profile = validateProfile(input), target = profile.environments.production;
  if (!target || profile.ci?.workflow !== 'ci.yml' || JSON.stringify(profile.ci.requiredJobs) !== JSON.stringify(['CI Gate'])
      || !Array.isArray(validation) || !validation.length || !validation.every(argv => Array.isArray(argv) && argv.length && argv.every(arg => typeof arg === 'string' && arg.length && !/[\r\n\0]/.test(arg)))) throw Error('DELIVERY_SCAFFOLD_INVALID');
  const workflow = join(directory, '.github', 'workflows');
  await mkdir(workflow, { recursive: true });
  const ci = `name: CI
on:
  pull_request:
  push:
    branches: [${JSON.stringify(profile.branch)}]
  workflow_dispatch:
permissions:
  contents: read
jobs:
  gate:
    name: CI Gate
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
        with:
          persist-credentials: false
      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020
        with:
          node-version: '22'
      - name: Verify the pinned Boney baseline
        run: node tools/release/boney/cli.mjs verify-snapshot tools/release/boney
      - name: Product validation
        run: |
          set -Eeuo pipefail
${validation.map(argv => `          ${argv.map(quote).join(' ')}`).join('\n')}
`;
  const release = `name: Product release
on:
  workflow_dispatch:
permissions:
  contents: read
  pull-requests: read
  actions: read
concurrency:
  group: product-release-production
  cancel-in-progress: false
jobs:
  release:
    runs-on: ubuntu-latest
    timeout-minutes: 60
    environment: ${JSON.stringify(target.githubEnvironment)}
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
        with:
          ref: \${{ github.sha }}
          persist-credentials: false
      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020
        with:
          node-version: '22'
      - name: Verify the pinned Boney baseline
        run: node tools/release/boney/cli.mjs verify-snapshot tools/release/boney
      - name: Run the shared chain with the product's hosting and durable store
        env:
          GITHUB_TOKEN: \${{ github.token }}
        run: >-
          node tools/release/boney/cli.mjs run
          --profile product.delivery.json --adapter tools/release/product-adapter.mjs
          --environment production --revision "$GITHUB_SHA"
          --receipt .evidence/delivery/release-result.json
      - uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9
        if: always()
        with:
          name: release-evidence-\${{ github.run_id }}-\${{ github.run_attempt }}
          path: .evidence/delivery/
          include-hidden-files: true
          if-no-files-found: error
`;
  await writeFile(join(directory, 'product.delivery.json'), JSON.stringify(profile, null, 2) + '\n', { flag: 'wx' });
  await writeFile(join(workflow, 'ci.yml'), ci, { flag: 'wx' });
  await writeFile(join(workflow, 'release.yml'), release, { flag: 'wx' });
}
