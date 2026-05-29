import type { ReactNode } from 'react';
import { CommandButton } from './CommandButton';

interface MarkdownRendererProps {
  markdown: string;
  className?: string;
  testId?: string;
}

interface CodeBlock {
  language: string | null;
  content: string;
}

export function MarkdownRenderer({ markdown, className, testId }: MarkdownRendererProps) {
  return (
    <div className={['markdown-renderer', className].filter(Boolean).join(' ')} data-testid={testId}>
      {renderBlocks(markdown)}
    </div>
  );
}

function renderBlocks(markdown: string) {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const blocks: ReactNode[] = [];

  for (let index = 0; index < lines.length;) {
    const line = lines[index];
    const trimmed = line.trim();

    if (!trimmed) {
      index += 1;
      continue;
    }

    if (trimmed.startsWith('```')) {
      const language = trimmed.slice(3).trim() || null;
      const content: string[] = [];
      index += 1;

      while (index < lines.length && !lines[index].trim().startsWith('```')) {
        content.push(lines[index]);
        index += 1;
      }

      if (index < lines.length) {
        index += 1;
      }

      blocks.push(<MarkdownCodeBlock key={`code-${index}-${blocks.length}`} language={language} content={content.join('\n')} />);
      continue;
    }

    const heading = /^(#{1,4})\s+(.+)$/.exec(trimmed);
    if (heading) {
      const level = heading[1].length;
      const children = renderInline(heading[2]);
      const key = `heading-${index}`;

      if (level === 1) {
        blocks.push(<h1 key={key}>{children}</h1>);
      } else if (level === 2) {
        blocks.push(<h2 key={key}>{children}</h2>);
      } else if (level === 3) {
        blocks.push(<h3 key={key}>{children}</h3>);
      } else {
        blocks.push(<h4 key={key}>{children}</h4>);
      }

      index += 1;
      continue;
    }

    if (isTableStart(lines, index)) {
      const tableLines: string[] = [lines[index], lines[index + 1]];
      index += 2;

      while (index < lines.length && isTableRow(lines[index])) {
        tableLines.push(lines[index]);
        index += 1;
      }

      blocks.push(renderTable(tableLines, `table-${index}-${blocks.length}`));
      continue;
    }

    if (/^[-*]\s+/.test(trimmed)) {
      const items: string[] = [];
      while (index < lines.length && /^[-*]\s+/.test(lines[index].trim())) {
        items.push(lines[index].trim().replace(/^[-*]\s+/, ''));
        index += 1;
      }

      blocks.push(
        <ul key={`ul-${index}-${blocks.length}`}>
          {items.map((item, itemIndex) => (
            <li key={`${item}-${itemIndex}`}>{renderInline(item)}</li>
          ))}
        </ul>
      );
      continue;
    }

    if (/^\d+\.\s+/.test(trimmed)) {
      const items: string[] = [];
      while (index < lines.length && /^\d+\.\s+/.test(lines[index].trim())) {
        items.push(lines[index].trim().replace(/^\d+\.\s+/, ''));
        index += 1;
      }

      blocks.push(
        <ol key={`ol-${index}-${blocks.length}`}>
          {items.map((item, itemIndex) => (
            <li key={`${item}-${itemIndex}`}>{renderInline(item)}</li>
          ))}
        </ol>
      );
      continue;
    }

    if (trimmed.startsWith('>')) {
      const quoteLines: string[] = [];
      while (index < lines.length && lines[index].trim().startsWith('>')) {
        quoteLines.push(lines[index].trim().replace(/^>\s?/, ''));
        index += 1;
      }

      blocks.push(<blockquote key={`quote-${index}-${blocks.length}`}>{renderInline(quoteLines.join(' '))}</blockquote>);
      continue;
    }

    const paragraph: string[] = [trimmed];
    index += 1;
    while (index < lines.length && lines[index].trim() && !startsSpecialBlock(lines, index)) {
      paragraph.push(lines[index].trim());
      index += 1;
    }

    blocks.push(<p key={`p-${index}-${blocks.length}`}>{renderInline(paragraph.join(' '))}</p>);
  }

  return blocks;
}

function MarkdownCodeBlock({ language, content }: CodeBlock) {
  return (
    <figure className="markdown-code-block" data-testid="markdown.codeBlock">
      <figcaption>
        <span>{language ?? 'code'}</span>
        <CommandButton
          type="button"
          variant="subtle"
          testId="markdown.code.copy"
          onClick={() => void navigator.clipboard?.writeText(content)}
        >
          Copy code
        </CommandButton>
      </figcaption>
      <pre>
        <code>{content}</code>
      </pre>
    </figure>
  );
}

function renderInline(text: string) {
  const parts = text.split(/(`[^`]+`)/g);
  return parts.map((part, index) => {
    if (part.startsWith('`') && part.endsWith('`') && part.length > 1) {
      return <code key={`${part}-${index}`}>{part.slice(1, -1)}</code>;
    }

    return <span key={`${part}-${index}`}>{part}</span>;
  });
}

function startsSpecialBlock(lines: string[], index: number) {
  const trimmed = lines[index].trim();
  return (
    trimmed.startsWith('```') ||
    /^(#{1,4})\s+/.test(trimmed) ||
    /^[-*]\s+/.test(trimmed) ||
    /^\d+\.\s+/.test(trimmed) ||
    trimmed.startsWith('>') ||
    isTableStart(lines, index)
  );
}

function isTableStart(lines: string[], index: number) {
  return index + 1 < lines.length && isTableRow(lines[index]) && isTableSeparator(lines[index + 1]);
}

function isTableRow(line: string) {
  return line.includes('|') && line.trim().length > 0;
}

function isTableSeparator(line: string) {
  return /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(line);
}

function renderTable(lines: string[], key: string) {
  const headers = splitTableRow(lines[0]);
  const rows = lines.slice(2).map(splitTableRow);

  return (
    <table key={key}>
      <thead>
        <tr>
          {headers.map((header, index) => (
            <th key={`${header}-${index}`}>{renderInline(header)}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, rowIndex) => (
          <tr key={`row-${rowIndex}`}>
            {row.map((cell, cellIndex) => (
              <td key={`${cell}-${cellIndex}`}>{renderInline(cell)}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function splitTableRow(line: string) {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());
}
