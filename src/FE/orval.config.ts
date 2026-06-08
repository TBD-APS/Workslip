import { defineConfig } from "orval";

var jsonUrl = import.meta.env.OPENAPI_URL
export default defineConfig({
  workslip: {
    input: {
      target: `${jsonUrl}/openapi/v1.json`,
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