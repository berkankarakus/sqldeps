local root = vim.fn.getcwd()
local source = root .. "/fixtures/broken.sql"

vim.cmd("edit " .. vim.fn.fnameescape(source))
vim.bo.filetype = "sql"

local client_id = vim.lsp.start({
  name = "sqldeps-neovim-smoke",
  cmd = { root .. "/bin/Release/net10.0/sqldeps", "lsp" },
  root_dir = root,
})
assert(client_id, "sqldeps LSP client did not start")

local received = vim.wait(5000, function()
  return #vim.diagnostic.get(0) > 0
end, 50)
assert(received, "sqldeps LSP did not publish diagnostics")
assert(vim.lsp.get_client_by_id(client_id), "sqldeps LSP client stopped unexpectedly")

local has_parse_error = false
for _, diagnostic in ipairs(vim.diagnostic.get(0)) do
  if diagnostic.message:find("Incorrect syntax", 1, true) then
    has_parse_error = true
    break
  end
end
assert(has_parse_error, "sqldeps LSP diagnostics were not decoded by Neovim")

print("sqldeps neovim smoke: ok")
vim.lsp.stop_client(client_id, true)
vim.cmd("qa!")
