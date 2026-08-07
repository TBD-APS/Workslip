interface ServiceWorkerContainer {
  getRegistrations(): Promise<ServiceWorkerRegistration[]>;
}
