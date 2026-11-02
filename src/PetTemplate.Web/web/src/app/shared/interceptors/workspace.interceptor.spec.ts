import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { WORKSPACE_HEADER, workspaceInterceptor } from './workspace.interceptor';
import { WorkspaceContextService } from '../core/workspace-context.service';

describe('workspaceInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([workspaceInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should add the active workspace to own API requests', async () => {
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-1');

    const request = firstValueFrom(http.get('/api/Users/GetProfiles'));
    const req = httpMock.expectOne('/api/Users/GetProfiles');
    req.flush([]);
    await request;

    expect(req.request.headers.get(WORKSPACE_HEADER)).toBe('ws-1');
  });

  it('should not send the workspace to foreign hosts', async () => {
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-1');

    const request = firstValueFrom(http.get('https://example.com/data'));
    const req = httpMock.expectOne('https://example.com/data');
    req.flush({});
    await request;

    expect(req.request.headers.has(WORKSPACE_HEADER)).toBeFalse();
  });

  it('should send nothing without an active workspace', async () => {
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId(null);

    const request = firstValueFrom(http.get('/api/Workspace/GetList'));
    const req = httpMock.expectOne('/api/Workspace/GetList');
    req.flush([]);
    await request;

    expect(req.request.headers.has(WORKSPACE_HEADER)).toBeFalse();
  });

  /** Переименование не активного пространства шлёт его заголовок само */
  it('should keep a workspace header set by the request itself', async () => {
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-1');

    const headers = new HttpHeaders({ [WORKSPACE_HEADER]: 'ws-2' });
    const request = firstValueFrom(http.post('/api/Workspace/Update', {}, { headers }));
    const req = httpMock.expectOne('/api/Workspace/Update');
    req.flush(null);
    await request;

    expect(req.request.headers.get(WORKSPACE_HEADER)).toBe('ws-2');
  });
});
