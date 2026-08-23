import { LinearClient } from "@linear/sdk";

const STALE_AFTER_DAYS = 30;
const STALE_LABEL_NAME = "stale";
const STALE_LABEL_COLOR = "#6B7280";
const DAY_MS = 24 * 60 * 60 * 1000;

function requireApiKey() {
  const apiKey = process.env.LINEAR_API_KEY?.trim();

  if (!apiKey) {
    throw new Error(
      "LINEAR_API_KEY is required. Configure it as a GitHub Actions repository secret."
    );
  }

  return apiKey;
}

function formatError(error) {
  if (error instanceof Error) {
    return error.message;
  }

  return String(error);
}

async function findOrCreateStaleLabel(client) {
  const labels = await client.paginate(
    (variables) => client.issueLabels(variables),
    { first: 50 }
  );

  const existingLabel = labels.find(
    (label) => label.name.toLowerCase() === STALE_LABEL_NAME
  );

  if (existingLabel) {
    console.log(
      `[linear-stale] Using existing label "${existingLabel.name}" (${existingLabel.id}).`
    );
    return existingLabel;
  }

  const payload = await client.issueLabelCreate({
    name: STALE_LABEL_NAME,
    color: STALE_LABEL_COLOR,
    description: `Backlog issue with no updates for at least ${STALE_AFTER_DAYS} days.`,
  });

  if (!payload.success || !payload.issueLabel) {
    throw new Error(`Could not create Linear label "${STALE_LABEL_NAME}".`);
  }

  console.log(
    `[linear-stale] Created label "${payload.issueLabel.name}" (${payload.issueLabel.id}).`
  );

  return payload.issueLabel;
}

async function findStaleBacklogIssues(client, cutoff) {
  return client.paginate(
    (variables) =>
      client.issues({
        ...variables,
        filter: {
          state: { name: { eq: "Backlog" } },
          updatedAt: { lt: cutoff.toISOString() },
        },
      }),
    { first: 50 }
  );
}

async function issueHasLabel(issue, labelId) {
  const labels = await issue.labels();
  return labels.nodes.some((label) => label.id === labelId);
}

async function main() {
  const client = new LinearClient({ apiKey: requireApiKey() });
  const cutoff = new Date(Date.now() - STALE_AFTER_DAYS * DAY_MS);

  console.log(
    `[linear-stale] Looking for Backlog issues last updated before ${cutoff.toISOString()}.`
  );

  const staleLabel = await findOrCreateStaleLabel(client);
  const issues = await findStaleBacklogIssues(client, cutoff);

  console.log(`[linear-stale] Found ${issues.length} candidate issue(s).`);

  let labeled = 0;
  let alreadyLabeled = 0;
  let failed = 0;

  for (const issue of issues) {
    try {
      if (await issueHasLabel(issue, staleLabel.id)) {
        alreadyLabeled += 1;
        console.log(
          `[linear-stale] SKIP ${issue.identifier}: already has "${STALE_LABEL_NAME}".`
        );
        continue;
      }

      const payload = await client.issueUpdate(issue.id, {
        addedLabelIds: [staleLabel.id],
      });

      if (!payload.success) {
        throw new Error("Linear returned success=false while updating the issue.");
      }

      labeled += 1;
      console.log(
        `[linear-stale] LABELED ${issue.identifier}: ${issue.title}`
      );
    } catch (error) {
      failed += 1;
      console.error(
        `[linear-stale] FAILED ${issue.identifier}: ${formatError(error)}`
      );
    }
  }

  console.log(
    `[linear-stale] Result: candidates=${issues.length}, labeled=${labeled}, alreadyStale=${alreadyLabeled}, failed=${failed}.`
  );

  if (failed > 0) {
    process.exitCode = 1;
  }
}

main().catch((error) => {
  console.error(`[linear-stale] Fatal: ${formatError(error)}`);
  process.exitCode = 1;
});
