import * as monaco from 'monaco-editor/editor/editor.api.js'
import 'monaco-editor/editor/contrib/suggest/browser/suggestController.js'
import { conf, language } from 'monaco-editor/languages/definitions/sql/sql.js'
import EditorWorker from 'monaco-editor/editor/editor.worker.js?worker'

globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() }
monaco.languages.register({ id: 'sql', extensions: ['.sql'], aliases: ['SQL'] })
monaco.languages.setLanguageConfiguration('sql', conf)
monaco.languages.setMonarchTokensProvider('sql', language)
monaco.languages.registerCompletionItemProvider('sql', {
  provideCompletionItems(model, position) {
    const word = model.getWordUntilPosition(position)
    const range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn)
    return {
      suggestions: ['SELECT', 'FROM', 'WHERE', 'WITH', 'JOIN', 'GROUP BY', 'HAVING', 'ORDER BY', 'LIMIT', 'COUNT', 'SUM', 'DISTINCT']
        .map(keyword => ({ label: keyword, insertText: keyword, kind: monaco.languages.CompletionItemKind.Keyword, range })),
    }
  },
})

export function mountSqlEditor(element, value, update) {
  const editor = monaco.editor.create(element, {
    value, language: 'sql', automaticLayout: true, minimap: { enabled: false }, scrollBeyondLastLine: false,
    lineNumbers: 'on', autoIndent: 'full', tabSize: 2, fontSize: 14, wordWrap: 'on',
    ariaLabel: 'SQL 查詢編輯器', ariaRequired: true, renderLineHighlight: 'line',
    padding: { top: 12, bottom: 12 }, quickSuggestions: true,
  })
  const change = editor.onDidChangeModelContent(() => update(editor.getValue()))
  return {
    setValue(text) { if (editor.getValue() !== text) editor.setValue(text) },
    dispose() {
      change.dispose()
      const model = editor.getModel()
      editor.dispose()
      model?.dispose()
    },
  }
}
