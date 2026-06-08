import { defineConfig } from "orval";


const openApiUrl = process.env.VITE_API_URL ?? '';

export default defineConfig({
  workslip: {
    input: {
      target: openApiUrl,
    },
    output: {
      mode: "tags-split",
      target: "./src/api/generated/workslip.ts",
      schemas: "./src/api/generated/models",
      client: "react-query",
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