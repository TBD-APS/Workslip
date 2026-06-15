import { defineConfig } from "orval";

export default defineConfig({
  workslip: {
    input: {
      target: "../openapi.json",
    },
    output: {
      mode: "tags-split",
      target: "./src/api/generated/workslip.ts",
      schemas: "./src/api/generated/models",
      client: "react-query",
      httpClient: "axios",
      clean: true,
      override: {
        mutator: {
          path: "src/api/fetcherOrval.ts",
          name: "customAxiosInstance",
        },
      },
    },
  },
});