let appLayoutModulePromise: Promise<typeof import('../components/layouts/AppLayout')> | null = null;
let jobListModulePromise: Promise<typeof import('../features/jobs/routes/JobList')> | null = null;

function loadAppLayoutModule() {
  if (!appLayoutModulePromise) {
    appLayoutModulePromise = import('../components/layouts/AppLayout').catch((error) => {
      appLayoutModulePromise = null;
      throw error;
    });
  }

  return appLayoutModulePromise;
}

function loadJobListModule() {
  if (!jobListModulePromise) {
    jobListModulePromise = import('../features/jobs/routes/JobList').catch((error) => {
      jobListModulePromise = null;
      throw error;
    });
  }

  return jobListModulePromise;
}

/**
 * Warm the authenticated shell and default jobs route without mounting them.
 * This runs only after a stored token exists, so the public Lighthouse path
 * stays lean while authenticated startup can fetch code in parallel with
 * session validation.
 */
export async function preloadPrimaryAppRoute(): Promise<void> {
  await Promise.all([
    loadAppLayoutModule(),
    loadJobListModule(),
  ]);
}
