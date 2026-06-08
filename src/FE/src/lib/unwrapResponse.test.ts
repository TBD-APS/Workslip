import { getResponseData } from './unwrapResponse';

type UserViewModel = {
  id: string;
  displayName: string;
  email: string;
  role: string;
};

type UserListViewModel = {
  users: UserViewModel[];
  total: number;
};

type GeneratedVoidResponse = {
  data: void;
  status: 200;
  headers: Headers;
};

const generatedResponse = {
  data: undefined as void,
  status: 200,
  headers: new Headers(),
} satisfies GeneratedVoidResponse;

const userList: UserListViewModel | undefined = getResponseData<UserListViewModel>(generatedResponse);

void userList;
