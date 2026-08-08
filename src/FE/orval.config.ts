import { defineConfig } from "orval";

const localApiBaseUrl = process.env.VITE_API_BASE_URL ?? "http://localhost:5262";
const openApiTarget = process.env.OPENAPI_DOCUMENT
  ?? `${localApiBaseUrl}/openapi/v1.json`;

export default defineConfig({
  workslip: {
    input: {
      target: openApiTarget,
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
