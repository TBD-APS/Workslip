import { defineConfig } from "orval";

const openApiUrl = process.env.VITE_API_BASE_URL ?? '';

export default defineConfig({
  workslip: {
    input: {
      target: openApiUrl + '/openapi/v1.json',
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