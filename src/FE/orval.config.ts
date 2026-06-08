import { defineConfig } from "orval";

const apiBaseUrl = import.meta.env.DEV
  ? "http://localhost:5262"
  : import.meta.env.VITE_API_BASE_URL;


export default defineConfig({
  workslip: {
    input: {
      target: `${apiBaseUrl}/openapi/v1.json`,
    },
    output: {
      mode: "tags-split",
      target: "./src/api/generated/workslip.ts",
      schemas: "./src/api/generated/models",
      client: "react-query",
      clean: true,
      override: {
        mutator: {
          path: "./src/api/fetcherOrval.ts",
          name: "customAxiosInstance",
        },
      },
    },
  },
});