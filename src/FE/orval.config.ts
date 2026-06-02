import { defineConfig } from "orval";

export default defineConfig({
  workslip: {
    input: {
      target: "http://localhost:5262/openapi/v1.json",
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