import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { salesforceErrorOf, SalesforceErrorDetails, ServerMessage } from './shared';

/** What Salesforce answered (ADR 0211) and the /limits warning (ADR 0210), in both languages. */
describe('Salesforce error details', () => {
  const params = {
    status: '400',
    call: 'GET /queryAll: SELECT Id, IsDeleted FROM User',
    errorCode: 'INVALID_FIELD',
    message: "No such column 'IsDeleted' on entity 'User'.",
  };

  it('reads the structured fields from message params only when a call is present', () => {
    expect(salesforceErrorOf(params)).toEqual(params);
    expect(salesforceErrorOf({ detail: '500 boom' })).toBeNull();
    expect(salesforceErrorOf(null)).toBeNull();
  });

  it.each([
    ['en', 'Salesforce refused the request.', 'HTTP status', 'Salesforce error code'],
    ['ar', 'رفض Salesforce الطلب.', 'رمز حالة HTTP', 'رمز خطأ Salesforce'],
  ] as const)(
    'shows the translated sentence and labelled technical values (%s)',
    async (language, sentence, status, code) => {
      await renderWithProviders(<ServerMessage messageKey="integration.sync.errors.http" params={params} />, {
        language,
      });
      expect(screen.getByText(sentence)).toBeInTheDocument();
      const details = screen.getByTestId('salesforce-error');
      expect(within(details).getByText(status)).toBeInTheDocument();
      expect(within(details).getByText(code)).toBeInTheDocument();
      // Salesforce's own values are shown as sent, isolated left-to-right inside an RTL page.
      const value = within(details).getByText('INVALID_FIELD');
      expect(value.tagName).toBe('BDI');
      expect(value).toHaveAttribute('dir', 'ltr');
      expect(within(details).getByText(params.call)).toBeInTheDocument();
      // No raw "(detail)" suffix when the structured fields are there.
      expect(screen.queryByText(/\(400/)).not.toBeInTheDocument();
    },
  );

  it('keeps the raw detail of runs stored before 1.0.1', async () => {
    await renderWithProviders(
      <ServerMessage
        messageKey="integration.sync.errors.http"
        params={{ detail: '403 [{"errorCode":"X"}]' }}
      />,
    );
    expect(screen.getByText('Salesforce refused the request. (403 [{"errorCode":"X"}])')).toBeInTheDocument();
    expect(screen.queryByTestId('salesforce-error')).not.toBeInTheDocument();
  });

  it('leaves out empty values', async () => {
    await renderWithProviders(
      <SalesforceErrorDetails
        error={{ status: null, call: 'GET /limits', errorCode: null, message: null }}
      />,
    );
    const details = screen.getByTestId('salesforce-error');
    expect(within(details).getAllByRole('definition')).toHaveLength(1);
  });

  it.each(['en', 'ar'] as const)(
    'words the query usage and target kind of validation issues (%s)',
    async (language) => {
      await renderWithProviders(
        <ServerMessage
          messageKey="integration.sync.validation.unknownField"
          params={{ field: 'Employee_Number__c', object: 'Contact', usage: 'people' }}
        />,
        { language },
      );
      expect(screen.getByText(language === 'en' ? /people matching/ : /مطابقة الأشخاص/)).toBeInTheDocument();
      await renderWithProviders(
        <ServerMessage
          messageKey="integration.sync.validation.valueNotConvertible"
          params={{ field: 'Impact_Level__c', values: 'low', targetKind: 'int' }}
        />,
        { language },
      );
      expect(screen.getByText(language === 'en' ? /whole number/ : /عدد صحيح/)).toBeInTheDocument();
    },
  );

  it('explains the refused API limits as a warning', async () => {
    await renderWithProviders(
      <ServerMessage
        messageKey="integration.sync.warnings.limitsRefused"
        params={{
          status: '403',
          call: 'GET /limits',
          errorCode: 'INSUFFICIENT_ACCESS',
          message: 'Insufficient privileges',
        }}
      />,
    );
    expect(screen.getByText(/View Setup and Configuration/)).toBeInTheDocument();
    expect(screen.getByText('INSUFFICIENT_ACCESS')).toBeInTheDocument();
  });
});
