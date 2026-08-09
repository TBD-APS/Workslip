import { customAxiosInstance } from '../../../api/fetcherOrval';

export type MonthlyHoursPdfPreview = {
  contentType: string;
  pages: string[];
};

export function getMonthlyHoursPdfPreview(year: number, month: number): Promise<MonthlyHoursPdfPreview> {
  return customAxiosInstance<MonthlyHoursPdfPreview>({
    url: '/api/worksheets/all/report/pdf/preview',
    method: 'GET',
    params: { year, month },
  });
}
