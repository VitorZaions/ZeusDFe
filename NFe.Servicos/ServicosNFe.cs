using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using DFe.Utils.Assinatura;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Servicos.AdmCsc;
using NFe.Classes.Servicos.Autorizacao;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.ConsultaCadastro;
using NFe.Classes.Servicos.DistribuicaoDFe;
using NFe.Classes.Servicos.Download;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Inutilizacao;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Recepcao.Retorno;
using NFe.Classes.Servicos.Status;
using NFe.Classes.Servicos.Tipos;
using NFe.Servicos.Retorno;
using NFe.Utils;
using NFe.Utils.AdmCsc;
using NFe.Utils.Autorizacao;
using NFe.Utils.Consulta;
using NFe.Utils.ConsultaCadastro;
using NFe.Utils.DistribuicaoDFe;
using NFe.Utils.Download;
using NFe.Utils.Evento;
using NFe.Utils.Excecoes;
using NFe.Utils.Inutilizacao;
using NFe.Utils.NFe;
using NFe.Utils.Recepcao;
using NFe.Utils.Status;
using NFe.Utils.Validacao;
using NFe.Wsdl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using NFe.Classes;
using Shared.DFe.Utils;
using FuncoesXml = DFe.Utils.FuncoesXml;
using System.Xml.Linq;
using NFe.Classes.Servicos.ConsultaGtin;
using NFe.Utils.ConsultaGtin;

namespace NFe.Servicos
{
    public sealed class ServicosNFe : IDisposable
    {
        private readonly X509Certificate2 _certificado;
        private readonly bool _controlarCertificado;
        private readonly ConfiguracaoServico _cFgServico;
        private readonly string _path;

        /// <summary>
        ///     Cria uma instância da Classe responsável pelos serviços relacionados à NFe
        /// </summary>
        /// <param name="cFgServico"></param>
        public ServicosNFe(ConfiguracaoServico cFgServico, X509Certificate2 certificado = null)
        {
            _cFgServico = cFgServico;
            _controlarCertificado = certificado == null;
            if (_controlarCertificado)
                _certificado = CertificadoDigital.ObterCertificado(cFgServico.Certificado);
            else
                _certificado = certificado;

            string path = Assembly.GetExecutingAssembly().Location;
            _path = string.IsNullOrEmpty(path) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(path);

            //Define a versão do protocolo de segurança
            ServicePointManager.SecurityProtocol = cFgServico.ProtocoloDeSeguranca;

            if (_cFgServico.ValidarCertificadoDoServidor)
                ServicePointManager.ServerCertificateValidationCallback = null;
            else
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        }

        public X509Certificate2 PegarCertificado
        {
            get
            {
                return this._certificado;
            }            
        }

        private string SalvarArquivoXml(string nomeArquivo, string xmlString)
        {
            if (!_cFgServico.SalvarXmlServicos) return null;
            var dir = string.IsNullOrEmpty(_cFgServico.DiretorioSalvarXml) ? _path : _cFgServico.DiretorioSalvarXml;
            var filename = Path.Combine(dir, nomeArquivo);
            var stw = new StreamWriter(filename);
            stw.WriteLine(xmlString);
            stw.Close();
            return filename;
        }

        private INfeServicoAutorizacao CriarServicoAutorizacao(ServicoNFe servico, bool compactarMensagem)
        {
            if (servico != ServicoNFe.NFeAutorizacao)
                throw new Exception(
                    string.Format("O serviço {0} não pode ser criado no método {1}!", servico,
                        MethodBase.GetCurrentMethod().Name));

            return ServicoNfeFactory.CriaWsdlAutorizacao(_cFgServico, _certificado, compactarMensagem);
        }

        private INfeServico CriarServico(ServicoNFe servico, string uf = null)
        {
            return ServicoNfeFactory.CriaWsdlOutros(servico, _cFgServico, _certificado, uf);
        }

        /// <summary>
        ///     Consulta o status do Serviço de NFe
        /// </summary>
        /// <returns>Retorna um objeto da classe RetornoNfeStatusServico com os dados status do serviço</returns>
        public RetornoNfeStatusServico NfeStatusServico(bool exceptionCompleta = false)
        {
            var versaoServico = ServicoNFe.NfeStatusServico.VersaoServicoParaString(_cFgServico.VersaoNfeStatusServico);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeStatusServico);

            if (_cFgServico.VersaoNfeStatusServico != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Cria o objeto consStatServ

            var pedStatus = new consStatServ
            {
                cUF = _cFgServico.cUF,
                tpAmb = _cFgServico.tpAmb,
                versao = versaoServico
            };

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlStatus = pedStatus.ObterXmlString();

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-ped-sta.xml", xmlStatus);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeStatusServico, _cFgServico.VersaoNfeStatusServico, xmlStatus, cfgServico: _cFgServico);

            var dadosStatus = new XmlDocument();
            dadosStatus.LoadXml(xmlStatus);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosStatus);
            }
            catch (WebException ex)
            {
                if (exceptionCompleta)
                {
                    throw;
                }
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeStatusServico, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retConsStatServ = new retConsStatServ().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-sta.xml", retornoXmlString);

            return new RetornoNfeStatusServico(pedStatus.ObterXmlString(), retConsStatServ.ObterXmlString(),
                retornoXmlString, retConsStatServ);

            #endregion
        }

        /// <summary>
        ///     Consulta a Situação da NFe
        /// </summary>
        /// <returns>Retorna um objeto da classe RetornoNfeConsultaProtocolo com os dados da Situação da NFe</returns>
        public RetornoNfeConsultaProtocolo NfeConsultaProtocolo(string chave)
        {
            var versaoServico = ServicoNFe.NfeConsultaProtocolo.VersaoServicoParaString(_cFgServico.VersaoNfeConsultaProtocolo);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeConsultaProtocolo);

            if (_cFgServico.VersaoNfeConsultaProtocolo != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Cria o objeto consSitNFe

            var pedConsulta = new consSitNFe
            {
                versao = versaoServico,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chave
            };

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlConsulta = pedConsulta.ObterXmlString();

            SalvarArquivoXml(chave + "-ped-sit.xml", xmlConsulta);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeConsultaProtocolo, _cFgServico.VersaoNfeConsultaProtocolo, xmlConsulta, cfgServico: _cFgServico);

            var dadosConsulta = new XmlDocument();
            dadosConsulta.LoadXml(xmlConsulta);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosConsulta);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeConsultaProtocolo, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retConsulta = new retConsSitNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(chave + "-sit.xml", retornoXmlString);

            return new RetornoNfeConsultaProtocolo(pedConsulta.ObterXmlString(), retConsulta.ObterXmlString(),
                retornoXmlString, retConsulta);

            #endregion
        }

        /// <summary>
        ///     Inutiliza uma faixa de números
        /// </summary>
        /// <param name="cnpj"></param>
        /// <param name="ano"></param>
        /// <param name="modelo"></param>
        /// <param name="serie"></param>
        /// <param name="numeroInicial"></param>
        /// <param name="numeroFinal"></param>
        /// <param name="justificativa"></param>
        /// <returns>Retorna um objeto da classe RetornoNfeInutilizacao com o retorno do serviço NfeInutilizacao</returns>
        public RetornoNfeInutilizacao NfeInutilizacao(string cnpj, int ano, ModeloDocumento modelo, int serie,
            int numeroInicial, int numeroFinal, string justificativa)
        {
            var versaoServico = ServicoNFe.NfeInutilizacao.VersaoServicoParaString(_cFgServico.VersaoNfeInutilizacao);

            #region Cria o objeto inutNFe

            var pedInutilizacao = new inutNFe
            {
                versao = versaoServico,
                infInut = new infInutEnv
                {
                    tpAmb = _cFgServico.tpAmb,
                    cUF = _cFgServico.cUF,
                    ano = ano,
                    CNPJ = cnpj,
                    mod = modelo,
                    serie = serie,
                    nNFIni = numeroInicial,
                    nNFFin = numeroFinal,
                    xJust = justificativa
                }
            };

            var numId = string.Concat((int)pedInutilizacao.infInut.cUF, pedInutilizacao.infInut.ano.ToString("D2"),
                pedInutilizacao.infInut.CNPJ, (int)pedInutilizacao.infInut.mod,
                pedInutilizacao.infInut.serie.ToString().PadLeft(3, '0'),
                pedInutilizacao.infInut.nNFIni.ToString().PadLeft(9, '0'),
                pedInutilizacao.infInut.nNFFin.ToString().PadLeft(9, '0'));

            pedInutilizacao.infInut.Id = "ID" + numId;

            pedInutilizacao.Assina(_certificado, _cFgServico.Certificado.SignatureMethodSignedXml, _cFgServico.Certificado.DigestMethodReference, _cFgServico.RemoverAcentos);

            #endregion

            return NfeInutilizacao(pedInutilizacao);
        }

        /// <summary>
        /// Inutilizar uma faíxa de números já assinado.
        /// </summary>
        /// <param name="pedInutilizacao"></param>
        /// <returns></returns>
        public RetornoNfeInutilizacao NfeInutilizacao(inutNFe pedInutilizacao)
        {
            var versaoServico = ServicoNFe.NfeInutilizacao.VersaoServicoParaString(_cFgServico.VersaoNfeInutilizacao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeInutilizacao);

            if (_cFgServico.VersaoNfeStatusServico != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlInutilizacao = _cFgServico.RemoverAcentos
                ? pedInutilizacao.ObterXmlString().RemoverAcentos()
                : pedInutilizacao.ObterXmlString();

            var numId = pedInutilizacao.infInut.Id.Replace("ID", "");
            SalvarArquivoXml(numId + "-ped-inu.xml", xmlInutilizacao);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeInutilizacao, _cFgServico.VersaoNfeInutilizacao, xmlInutilizacao, cfgServico: _cFgServico);

            var dadosInutilizacao = new XmlDocument();
            dadosInutilizacao.LoadXml(xmlInutilizacao);


            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosInutilizacao);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeInutilizacao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retInutNFe = new retInutNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(numId + "-inu.xml", retornoXmlString);

            return new RetornoNfeInutilizacao(xmlInutilizacao, retInutNFe.ObterXmlString(),
                retornoXmlString, retInutNFe);

            #endregion
        }

        /// <summary>
        ///     Envia um evento genérico
        /// </summary>
        /// <param name="idlote"></param>
        /// <param name="eventos"></param>
        /// <param name="servicoEvento">Tipo de serviço do evento: valores válidos: RecepcaoEventoCancelmento, RecepcaoEventoCartaCorrecao, RecepcaoEventoEpec e RecepcaoEventoManifestacaoDestinatario</param>
        /// <param name="versaoEvento">Versão do serviço para o evento</param>
        /// <returns>Retorna um objeto da classe RetornoRecepcaoEvento com o retorno do serviço RecepcaoEvento</returns>
        private RetornoRecepcaoEvento RecepcaoEvento(int idlote, List<evento> eventos, ServicoNFe servicoEvento, VersaoServico versaoEvento, bool assinar)
        {
            var listaEventos = new List<ServicoNFe>
            {
                ServicoNFe.RecepcaoEventoCartaCorrecao,
                ServicoNFe.RecepcaoEventoCancelmento,
                ServicoNFe.RecepcaoEventoEpec,
                ServicoNFe.RecepcaoEventoManifestacaoDestinatario,
                ServicoNFe.RecepcaoEventoInsucessoEntregaNFe,
                ServicoNFe.RecepcaoEventoCancInsucessoEntregaNFe,
                ServicoNFe.RecepcaoEventoComprovanteEntregaNFe,
                ServicoNFe.RecepcaoEventoCancComprovanteEntregaNFe,
                ServicoNFe.RecepcaoEventoConciliacaoFinanceiraNFe,
                ServicoNFe.RecepcaoEventoCancConciliacaoFinanceiraNFe
            };
            if (
                !listaEventos.Contains(servicoEvento))
                throw new Exception(
                    string.Format("Serviço {0} é inválido para o método {1}!\nServiços válidos: \n • {2}", servicoEvento,
                        MethodBase.GetCurrentMethod().Name, string.Join("\n • ", listaEventos.ToArray())));

            var versaoServico = servicoEvento.VersaoServicoParaString(versaoEvento);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(servicoEvento);

            if (_cFgServico.VersaoRecepcaoEventoCceCancelamento != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Cria o objeto envEvento

            var pedEvento = new envEvento
            {
                versao = versaoServico,
                idLote = idlote,
                evento = eventos
            };

            if (assinar)
            {
                foreach (var evento in eventos)
                {
                    evento.infEvento.Id = "ID" + ((int)evento.infEvento.tpEvento) + evento.infEvento.chNFe +
                                        evento.infEvento.nSeqEvento.ToString().PadLeft(2, '0');
                    evento.Assina(_certificado, _cFgServico.Certificado.SignatureMethodSignedXml, _cFgServico.Certificado.DigestMethodReference, _cFgServico.RemoverAcentos);
                }
            }

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlEvento = _cFgServico.RemoverAcentos
                    ? pedEvento.ObterXmlString().RemoverAcentos()
                    : pedEvento.ObterXmlString();

            SalvarArquivoXml(idlote + "-ped-eve.xml", xmlEvento);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(servicoEvento, _cFgServico.VersaoRecepcaoEventoCceCancelamento, xmlEvento, cfgServico: _cFgServico);

            var dadosEvento = new XmlDocument();
            dadosEvento.LoadXml(xmlEvento);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosEvento);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(servicoEvento, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retEnvEvento = new retEnvEvento().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(idlote + "-eve.xml", retornoXmlString);

            #region Obtém um procEventoNFe de cada evento e salva em arquivo

            var listprocEventoNFe = new List<procEventoNFe>();

            foreach (var evento in eventos)
            {
                var eve = evento;
                var retEvento = (from retevento in retEnvEvento.retEvento
                                 where
                                 retevento.infEvento.chNFe == eve.infEvento.chNFe &&
                                 retevento.infEvento.tpEvento == eve.infEvento.tpEvento
                                 select retevento).SingleOrDefault();

                var procevento = new procEventoNFe { evento = eve, versao = eve.versao, retEvento = retEvento };
                listprocEventoNFe.Add(procevento);
                if (!_cFgServico.SalvarXmlServicos) continue;
                var proceventoXmlString = procevento.ObterXmlString();
                SalvarArquivoXml(procevento.evento.infEvento.Id.Substring(2) + "-procEventoNFe.xml", proceventoXmlString);
            }

            #endregion

            return new RetornoRecepcaoEvento(xmlEvento, retEnvEvento.ObterXmlString(), retornoXmlString,
                retEnvEvento, listprocEventoNFe);

            #endregion
        }

        /// <summary>
        ///     Envia um evento do tipo "Cancelamento"
        /// </summary>
        /// <returns>Retorna um objeto da classe <see cref="RetornoRecepcaoEvento"/> com o retorno do serviço <see cref="RecepcaoEvento"/></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancelamento(int idlote, int sequenciaEvento,
            string protocoloAutorizacao, string chaveNFe, string justificativa, string cpfcnpj, DateTimeOffset? dhEvento = null)
        {
            return RecepcaoEventoCancelamento(NFeTipoEvento.TeNfeCancelamento, idlote, sequenciaEvento,
                protocoloAutorizacao, chaveNFe, justificativa, cpfcnpj, dhEvento: dhEvento);
        }

        /// <summary>
        /// Envia eventos do tipo "Cancelamento" já assinado.
        /// </summary>
        /// <param name="idlote"></param>
        /// <param name="eventos"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancelamento(int idlote, List<evento> eventos)
        {
            var retorno = RecepcaoEvento(idlote, eventos, ServicoNFe.RecepcaoEventoCancelmento, _cFgServico.VersaoRecepcaoEventoCceCancelamento, false);
            return retorno;
        }

        /// <summary>
        ///     Envia um evento do tipo "Cancelamento por substituição"
        /// </summary>
        /// <returns>Retorna um objeto da classe <see cref="RetornoRecepcaoEvento"/> com o retorno do serviço <see cref="RecepcaoEvento"/></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancelamentoPorSubstituicao(int idlote, int sequenciaEvento,
            string protocoloAutorizacao, string chaveNFe, string justificativa, string cpfcnpj, Estado ufAutor, string versaoAplicativo, string chaveNfeSubstituta, DateTimeOffset? dhEvento = null)
        {
            return RecepcaoEventoCancelamento(NFeTipoEvento.TeNfeCancelamentoSubstituicao, idlote, sequenciaEvento,
                protocoloAutorizacao, chaveNFe, justificativa, cpfcnpj, ufAutor, TipoAutor.taEmpresaEmitente, versaoAplicativo, chaveNfeSubstituta, dhEvento: dhEvento);
        }

        private RetornoRecepcaoEvento RecepcaoEventoCancelamento(NFeTipoEvento tipoEventoCancelamento, int idlote,
            int sequenciaEvento, string protocoloAutorizacao, string chaveNFe, string justificativa, string cpfcnpj,
            Estado? ufAutor = null, TipoAutor? tipoAutor = null, string versaoAplicativo = null, string chaveNfeSubstituta = null, DateTimeOffset? dhEvento = null)
        {
            if (!NFeTipoEventoUtils.NFeTipoEventoCancelamento.Contains(tipoEventoCancelamento))
                throw new Exception(string.Format("Informe um dos seguintes tipos de eventos: {0}",
                    string.Join(", ",
                        NFeTipoEventoUtils.NFeTipoEventoCancelamento.Select(n => n.Descricao()))));

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoCceCancelamento);

            var detEvento = new detEvento
            {
                nProt = protocoloAutorizacao,
                versao = versaoServico,
                xJust = justificativa,
                descEvento = tipoEventoCancelamento.Descricao(),
                cOrgaoAutor = ufAutor,
                tpAutor = tipoAutor,
                verAplic = versaoAplicativo,
                chNFeRef = chaveNfeSubstituta
            };
            var infEvento = new infEventoEnv
            {
                cOrgao = _cFgServico.cUF,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = tipoEventoCancelamento,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoCancelmento, _cFgServico.VersaoRecepcaoEventoCceCancelamento, true);
            return retorno;
        }

        /// <summary>
        ///     Envia um evento do tipo "Carta de Correção"
        /// </summary>
        /// <returns>Retorna um objeto da classe <see cref="RetornoRecepcaoEvento"/> com o retorno do serviço <see cref="RecepcaoEvento"/></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCartaCorrecao(int idlote, int sequenciaEvento, string chaveNFe,
            string correcao, string cpfcnpj, DateTimeOffset? dhEvento = null)
        {
            var versaoServico =
                ServicoNFe.RecepcaoEventoCartaCorrecao.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoCceCancelamento);
            var detEvento = new detEvento
            {
                versao = versaoServico,
                xCorrecao = correcao,
                xJust = null,
                descEvento = NFeTipoEvento.TeNfeCartaCorrecao.Descricao(),
                xCondUso =
                    "A Carta de Correção é disciplinada pelo § 1º-A do art. 7º do Convênio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularização de erro ocorrido na emissão de documento fiscal, desde que o erro não esteja relacionado com: I - as variáveis que determinam o valor do imposto tais como: base de cálculo, alíquota, diferença de preço, quantidade, valor da operação ou da prestação; II - a correção de dados cadastrais que implique mudança do remetente ou do destinatário; III - a data de emissão ou de saída."
            };

            if (_cFgServico.cUF == Estado.MT || _cFgServico.RemoverAcentos)
            {
                detEvento.descEvento = "Carta de Correcao";
                detEvento.xCondUso =
                    "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.";
            }
            var infEvento = new infEventoEnv
            {
                cOrgao = _cFgServico.cUF,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeCartaCorrecao,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoCartaCorrecao, _cFgServico.VersaoRecepcaoEventoCceCancelamento, true);
            return retorno;
        }

        /// <summary>
        /// Envia eventos do tipo "Carta de correção" já assinado.
        /// </summary>
        /// <param name="idlote"></param>
        /// <param name="eventos"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCartaCorrecao(int idlote, List<evento> eventos)
        {
            var retorno = RecepcaoEvento(idlote, eventos, ServicoNFe.RecepcaoEventoCartaCorrecao, _cFgServico.VersaoRecepcaoEventoCceCancelamento, false);
            return retorno;
        }

        public RetornoRecepcaoEvento RecepcaoEventoManifestacaoDestinatario(int idlote, int sequenciaEvento,
                    string chaveNFe, NFeTipoEvento nFeTipoEventoManifestacaoDestinatario, string cpfcnpj,
                    string justificativa = null, DateTimeOffset? dhEvento = null)
        {
            return RecepcaoEventoManifestacaoDestinatario(idlote, sequenciaEvento, new[] { chaveNFe },
                nFeTipoEventoManifestacaoDestinatario, cpfcnpj, justificativa, dhEvento: dhEvento);
        }

        public RetornoRecepcaoEvento RecepcaoEventoManifestacaoDestinatario(int idlote, int sequenciaEvento,
            string[] chavesNFe, NFeTipoEvento nFeTipoEventoManifestacaoDestinatario, string cpfcnpj,
            string justificativa = null, DateTimeOffset? dhEvento = null)
        {
            if (!NFeTipoEventoUtils.NFeTipoEventoManifestacaoDestinatario.Contains(nFeTipoEventoManifestacaoDestinatario))
                throw new Exception(string.Format("Informe um dos seguintes tipos de eventos: {0}",
                    string.Join(", ",
                        NFeTipoEventoUtils.NFeTipoEventoManifestacaoDestinatario.Select(n => n.Descricao()))));

            var versaoServico =
                ServicoNFe.RecepcaoEventoManifestacaoDestinatario.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoManifestacaoDestinatario);
            var detEvento = new detEvento
            {
                versao = versaoServico,
                xJust = justificativa,
                descEvento = nFeTipoEventoManifestacaoDestinatario.Descricao()
            };

            var eventos = new List<evento>();
            foreach (var chaveNFe in chavesNFe)
            {
                var infEvento = new infEventoEnv
                {
                    cOrgao = Estado.AN,
                    tpAmb = _cFgServico.tpAmb,
                    chNFe = chaveNFe,
                    dhEvento = dhEvento ?? DateTime.Now,
                    tpEvento = nFeTipoEventoManifestacaoDestinatario,
                    nSeqEvento = sequenciaEvento,
                    verEvento = versaoServico,
                    detEvento = detEvento
                };
                if (cpfcnpj.Length == 11)
                    infEvento.CPF = cpfcnpj;
                else
                    infEvento.CNPJ = cpfcnpj;

                eventos.Add(new evento { versao = versaoServico, infEvento = infEvento });
            }


            var retorno = RecepcaoEvento(idlote, eventos,
                ServicoNFe.RecepcaoEventoManifestacaoDestinatario, _cFgServico.VersaoRecepcaoEventoManifestacaoDestinatario, true);
            return retorno;
        }

        /// <summary>
        ///     Envia um evento do tipo "EPEC"
        /// </summary>
        /// <param name="idlote"></param>
        /// <param name="sequenciaEvento"></param>
        /// <param name="nfe"></param>
        /// <param name="veraplic"></param>
        /// <returns>Retorna um objeto da classe RetornoRecepcaoEvento com o retorno do serviço RecepcaoEvento</returns>
        public RetornoRecepcaoEvento RecepcaoEventoEpec(int idlote, int sequenciaEvento, Classes.NFe nfe,
            string veraplic, DateTimeOffset? dhEvento = null)
        {
            var versaoServico =
                ServicoNFe.RecepcaoEventoEpec.VersaoServicoParaString(_cFgServico.VersaoRecepcaoEventoEpec);

            if (string.IsNullOrEmpty(nfe.infNFe.Id))
                nfe.Assina().Valida();

            var detevento = new detEvento
            {
                versao = versaoServico,
                cOrgaoAutor = nfe.infNFe.ide.cUF,
                tpAutor = TipoAutor.taEmpresaEmitente,
                verAplic = veraplic,
                descEvento = NFeTipoEvento.TeNfceEpec.Descricao(),
                dhEmi = nfe.infNFe.ide.dhEmi,
                tpNF = nfe.infNFe.ide.tpNF,
                IE = nfe.infNFe.emit.IE,
                dest = new dest
                {
                    UF = nfe.infNFe.dest.enderDest.UF,
                    CNPJ = nfe.infNFe.dest.CNPJ,
                    CPF = nfe.infNFe.dest.CPF,
                    IE = nfe.infNFe.dest.IE,
                    vNF = nfe.infNFe.total.ICMSTot.vNF,
                    vICMS = nfe.infNFe.total.ICMSTot.vICMS,
                    vST = nfe.infNFe.total.ICMSTot.vST
                }
            };

            var infEvento = new infEventoEnv
            {
                cOrgao = Estado.AN,
                tpAmb = nfe.infNFe.ide.tpAmb,
                CNPJ = nfe.infNFe.emit.CNPJ,
                CPF = nfe.infNFe.emit.CPF,
                chNFe = nfe.infNFe.Id.Substring(3),
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfceEpec,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detevento
            };

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoEpec, _cFgServico.VersaoRecepcaoEventoEpec, true);
            return retorno;
        }

        /// <summary>
        /// Envia eventos do tipo "EPEC" já assinado
        /// </summary>
        /// <param name="idlote"></param>
        /// <param name="eventos"></param>
        /// <returns>Retorna um objeto da classe RetornoRecepcaoEvento com o retorno do serviço RecepcaoEvento</returns>
        public RetornoRecepcaoEvento RecepcaoEventoEpec(int idlote, List<evento> eventos)
        {
            var retorno = RecepcaoEvento(idlote, eventos, ServicoNFe.RecepcaoEventoCartaCorrecao, _cFgServico.VersaoRecepcaoEventoCceCancelamento, false);
            return retorno;
        }

        /// <summary>
        /// Recepção do Evento de Insucesso na Entrega
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="dhTentativaEntrega"></param>
        /// <param name="motivo">preencher com Enum MotivoInsucesso<see cref="MotivoInsucesso"/></param>
        /// <param name="hashTentativaEntrega">Hash SHA-1, no formato Base64, resultante da 
        /// concatenação de: Chave de Acesso da NF-e + Base64
        /// da imagem capturada na tentativa da entrega(ex: 
        /// imagem capturada da assinatura eletrônica, digital do 
        /// recebedor, foto, etc).</param>
        /// <param name="nTentativa"></param>
        /// <param name="dhHashTentativaEntrega"></param>
        /// <param name="latGps">Latitude do ponto de entrega (não obrigatório) </param>
        /// <param name="longGps">Longitude do ponto de entrega (não obrigatório)</param>
        /// <param name="justificativa">Preencher apenas se o motivo for outros <see cref="MotivoInsucesso.Outros"/> </param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoInsucessoEntrega(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, DateTimeOffset dhTentativaEntrega, MotivoInsucesso motivo, string hashTentativaEntrega, 
            int? nTentativa = null, DateTimeOffset? dhHashTentativaEntrega = null,  decimal? latGps = null, decimal? longGps = null,
            string justificativa = null, Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoInsucessoEntrega);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeInsucessoNaEntregadaNFe.Descricao(),
                cOrgaoAutor = ufAutor ?? _cFgServico.cUF,
                verAplic = versaoAplicativo ?? "1.0",
                dhTentativaEntrega = dhTentativaEntrega,
                nTentativa = nTentativa,
                tpMotivo = motivo,
                xJustMotivo = justificativa,
                latGPS = latGps,
                longGPS = longGps,
                hashTentativaEntrega = hashTentativaEntrega,
                dhHashTentativaEntrega = dhHashTentativaEntrega
            };
            var infEvento = new infEventoEnv
            {
                cOrgao = Estado.SVRS,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeInsucessoNaEntregadaNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoInsucessoEntregaNFe, _cFgServico.VersaoRecepcaoEventoInsucessoEntrega, true);
            return retorno;
        }

        /// <summary>
        /// Serviço para cancelamento insucesso na entrega
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="nProtEvento">Protocolo do eveento de insucesso na entrega que deseja cancelar</param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancInsucessoEntrega(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, string nProtEvento, 
            Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoInsucessoEntrega);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeCancInsucessoNaEntregadaNFe.Descricao(),
                cOrgaoAutor = ufAutor ?? _cFgServico.cUF,
                verAplic = versaoAplicativo ?? "1.0",
                nProtEvento = nProtEvento
            };

            var infEvento = new infEventoEnv
            {
                cOrgao = Estado.SVRS,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeCancInsucessoNaEntregadaNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoCancInsucessoEntregaNFe, _cFgServico.VersaoRecepcaoEventoInsucessoEntrega, true);
            return retorno;
        }

        /// <summary>
        /// Recepção do Evento de Comprovante de Entrega
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="dhEntrega"></param>
        /// <param name="nDoc">Número documento de identificação da pessoa</param>
        /// <param name="xNome">Nome da pessoa</param>
        /// <param name="hashComprovante">Hash SHA-1, no formato Base64, resultante da 
        /// concatenação de: Chave de Acesso da NF-e + Base64
        /// da imagem capturada do comprovante de entrega (ex: 
        /// imagem capturada da assinatura eletrônica, digital do 
        /// recebedor, foto, etc).</param>
        /// <param name="dhHashComprovante"></param>
        /// <param name="latGps">Latitude do ponto de entrega (não obrigatório) </param>
        /// <param name="longGps">Longitude do ponto de entrega (não obrigatório)</param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoComprovanteEntrega(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, DateTimeOffset dhEntrega, string nDoc, string xNome, string hashComprovante,
            DateTimeOffset? dhHashComprovante = null, decimal? latGps = null, decimal? longGps = null,
            Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoComprovanteEntrega);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeComprovanteDeEntregadaNFe.Descricao(),
                cOrgaoAutor = ufAutor ?? _cFgServico.cUF,
                tpAutor = TipoAutor.taEmpresaEmitente,
                verAplic = versaoAplicativo ?? "1.0",
                dhEntrega = dhEntrega,
                nDoc = nDoc,
                xNome = xNome,
                latGPS = latGps,
                longGPS = longGps,
                hashComprovante = hashComprovante,
                dhHashComprovante = dhHashComprovante
            };
            var infEvento = new infEventoEnv
            {
                cOrgao = Estado.AN,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeComprovanteDeEntregadaNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoComprovanteEntregaNFe, _cFgServico.VersaoRecepcaoEventoComprovanteEntrega, true);
            return retorno;
        }

        /// <summary>
        /// Serviço para cancelamento comprovante de entrega
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="nProtEvento">Protocolo do evento de comprovante de entrega que deseja cancelar</param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancComprovanteEntrega(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, string nProtEvento,
            Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoComprovanteEntrega);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeCancComprovanteDeEntregadaNFe.Descricao(),
                cOrgaoAutor = ufAutor ?? _cFgServico.cUF,
                tpAutor = TipoAutor.taEmpresaEmitente,
                verAplic = versaoAplicativo ?? "1.0",
                nProtEvento = nProtEvento
            };

            var infEvento = new infEventoEnv
            {
                cOrgao = Estado.AN,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeCancComprovanteDeEntregadaNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoCancComprovanteEntregaNFe, _cFgServico.VersaoRecepcaoEventoComprovanteEntrega, true);
            return retorno;
        }


        /// <summary>
        /// Recepção do Evento de Conciliação Financeira
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="pagamentos">Informações dos pagamentos</param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoConciliacaoFinanceira(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, List<detPagEvento> pagamentos,
            Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoConciliacaoFinanceira);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeConciliacaoFinanceiraNFe.Descricao(),
                verAplic = versaoAplicativo ?? "1.0",
                detPag = pagamentos
            };
            var infEvento = new infEventoEnv
            {
                cOrgao = (_cFgServico.ModeloDocumento == ModeloDocumento.NFCe ? _cFgServico.cUF : Estado.SVRS),
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeConciliacaoFinanceiraNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoConciliacaoFinanceiraNFe, _cFgServico.VersaoRecepcaoEventoConciliacaoFinanceira, true);
            return retorno;
        }

        /// <summary>
        /// Serviço para cancelamento Conciliação Financeira
        /// </summary>
        /// <param name="idlote">Nº do lote</param>
        /// <param name="sequenciaEvento">sequencia do evento</param>
        /// <param name="cpfcnpj"></param>
        /// <param name="chaveNFe"></param>
        /// <param name="nProtEvento">Protocolo do evento de conciliação financeira que deseja cancelar</param>
        /// <param name="ufAutor"></param>
        /// <param name="versaoAplicativo"></param>
        /// <param name="dhEvento"></param>
        /// <returns></returns>
        public RetornoRecepcaoEvento RecepcaoEventoCancConciliacaoFinanceira(int idlote,
            int sequenciaEvento, string cpfcnpj, string chaveNFe, string nProtEvento,
            Estado? ufAutor = null, string versaoAplicativo = null, DateTimeOffset? dhEvento = null)
        {

            var versaoServico =
                ServicoNFe.RecepcaoEventoCancelmento.VersaoServicoParaString(
                    _cFgServico.VersaoRecepcaoEventoConciliacaoFinanceira);

            var detEvento = new detEvento
            {
                versao = versaoServico,
                descEvento = NFeTipoEvento.TeNfeCancConciliacaoFinanceiraNFe.Descricao(),
                verAplic = versaoAplicativo ?? "1.0",
                nProtEvento = nProtEvento
            };

            var infEvento = new infEventoEnv
            {
                cOrgao = (_cFgServico.ModeloDocumento == ModeloDocumento.NFCe ? _cFgServico.cUF : Estado.SVRS),
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaveNFe,
                dhEvento = dhEvento ?? DateTime.Now,
                tpEvento = NFeTipoEvento.TeNfeCancConciliacaoFinanceiraNFe,
                nSeqEvento = sequenciaEvento,
                verEvento = versaoServico,
                detEvento = detEvento
            };
            if (cpfcnpj.Length == 11)
                infEvento.CPF = cpfcnpj;
            else
                infEvento.CNPJ = cpfcnpj;

            var evento = new evento { versao = versaoServico, infEvento = infEvento };

            var retorno = RecepcaoEvento(idlote, new List<evento> { evento }, ServicoNFe.RecepcaoEventoCancConciliacaoFinanceiraNFe, _cFgServico.VersaoRecepcaoEventoConciliacaoFinanceira, true);
            return retorno;
        }

        /// <summary>
        ///     Consulta a situação cadastral, com base na UF/Documento
        ///     <para>O documento pode ser: IE, CNPJ ou CPF</para>
        /// </summary>
        /// <param name="uf">Sigla da UF consultada, informar 'SU' para SUFRAMA</param>
        /// <param name="tipoDocumento">Tipo de documento a ser consultado</param>
        /// <param name="documento">Documento a ser consultado</param>
        /// <returns>Retorna um objeto da classe RetornoNfeConsultaCadastro com o retorno do serviço NfeConsultaCadastro</returns>
        public RetornoNfeConsultaCadastro NfeConsultaCadastro(string uf, ConsultaCadastroTipoDocumento tipoDocumento, string documento)
        {
            var versaoServico = ServicoNFe.NfeConsultaCadastro.VersaoServicoParaString(_cFgServico.VersaoNfeConsultaCadastro);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeConsultaCadastro, uf);

            if (_cFgServico.VersaoNfeConsultaCadastro != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Cria o objeto ConsCad

            var pedConsulta = new ConsCad
            {
                versao = versaoServico,
                infCons = new infConsEnv { UF = uf }
            };

            switch (tipoDocumento)
            {
                case ConsultaCadastroTipoDocumento.Ie:
                    pedConsulta.infCons.IE = documento;
                    break;
                case ConsultaCadastroTipoDocumento.Cnpj:
                    pedConsulta.infCons.CNPJ = documento;
                    break;
                case ConsultaCadastroTipoDocumento.Cpf:
                    pedConsulta.infCons.CPF = documento;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("tipoDocumento", tipoDocumento, null);
            }

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlConsulta = pedConsulta.ObterXmlString();

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-ped-cad.xml", xmlConsulta);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeConsultaCadastro, _cFgServico.VersaoNfeConsultaCadastro, xmlConsulta, cfgServico: _cFgServico);

            var dadosConsulta = new XmlDocument();
            dadosConsulta.LoadXml(xmlConsulta);


            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosConsulta);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeConsultaCadastro, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retConsulta = new retConsCad().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-cad.xml", retornoXmlString);

            return new RetornoNfeConsultaCadastro(pedConsulta.ObterXmlString(), retConsulta.ObterXmlString(),
                retornoXmlString, retConsulta);

            #endregion
        }

        public RetornoConsultaGtin ConsultaGtin(string gtin)
        {
            #region Cria o objeto wdsl para consulta
            var ws = CriarServico(ServicoNFe.ConsultaGtin);
            #endregion

            #region Cria o objeto consGTIN

            var consGtin = new consGTIN
            {
                versao = "1.00",
                GTIN = gtin
            };

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlConsulta = consGtin.ObterXmlString();

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-cons-gtin.xml", xmlConsulta);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.ConsultaGtin, _cFgServico.VersaoNfeConsultaCadastro, xmlConsulta, cfgServico: _cFgServico);

            var dadosConsulta = new XmlDocument();
            dadosConsulta.LoadXml(xmlConsulta);


            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosConsulta);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeConsultaCadastro, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retConsulta = new retConsGTIN().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-consGtin.xml", retornoXmlString);

            return new RetornoConsultaGtin(consGtin.ObterXmlString(), retConsulta.ObterXmlString(),
                retornoXmlString, retConsulta);

            #endregion
        }

        /// <summary>
        /// Serviço destinado à distribuição de informações resumidas e documentos fiscais eletrônicos de interesse de um ator, seja este pessoa física ou jurídica.
        /// </summary>
        /// <param name="ufAutor">Código da UF do Autor</param>
        /// <param name="documento">CNPJ/CPF do interessado no DF-e</param>
        /// <param name="ultNSU">Último NSU recebido pelo Interessado</param>
        /// <param name="nSU">Número Sequencial Único</param>
        /// <param name="chNFE">Chave eletronica da NF-e</param>
        /// <returns>Retorna um objeto da classe RetornoNfeDistDFeInt com os documentos de interesse do CNPJ/CPF pesquisado</returns>
        public RetornoNfeDistDFeInt NfeDistDFeInteresse(string ufAutor, string documento, string ultNSU = "0", string nSU = "0", string chNFE = "")
        {
            var versaoServico = ServicoNFe.NFeDistribuicaoDFe.VersaoServicoParaString(_cFgServico.VersaoNFeDistribuicaoDFe);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NFeDistribuicaoDFe);
            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                versaoDados = versaoServico
            };

            #endregion

            #region Cria o objeto distDFeInt

            var pedDistDFeInt = new distDFeInt
            {
                versao = versaoServico,
                tpAmb = _cFgServico.tpAmb,
                cUFAutor = _cFgServico.cUF
            };

            if (documento.Length == 11)
                pedDistDFeInt.CPF = documento;
            if (documento.Length > 11)
                pedDistDFeInt.CNPJ = documento;

            if (string.IsNullOrEmpty(chNFE))
                pedDistDFeInt.distNSU = new distNSU { ultNSU = ultNSU.PadLeft(15, '0') };

            if (!nSU.Equals("0"))
            {
                pedDistDFeInt.consNSU = new consNSU { NSU = nSU.PadLeft(15, '0') };
                pedDistDFeInt.distNSU = null;
                pedDistDFeInt.consChNFe = null;
            }

            if (!string.IsNullOrEmpty(chNFE))
            {
                pedDistDFeInt.consChNFe = new consChNFe { chNFe = chNFE };
                pedDistDFeInt.consNSU = null;
                pedDistDFeInt.distNSU = null;
            }

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlConsulta = pedDistDFeInt.ObterXmlString();

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-ped-DistDFeInt.xml", xmlConsulta);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NFeDistribuicaoDFe, _cFgServico.VersaoNFeDistribuicaoDFe, xmlConsulta, cfgServico: _cFgServico);

            var dadosConsulta = new XmlDocument();
            dadosConsulta.LoadXml(xmlConsulta);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosConsulta);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NFeDistribuicaoDFe, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retConsulta = new retDistDFeInt().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(DateTime.Now.ParaDataHoraString() + "-distDFeInt.xml", retornoXmlString);

            #region Obtém um retDistDFeInt de cada evento, adiciona os documentos ao resultado e salva-os em arquivo

            if (retConsulta.loteDistDFeInt != null && _cFgServico.UnZip)
            {
                foreach (var dFeInt in retConsulta.loteDistDFeInt)
                {
                    var conteudo = Compressao.Unzip(dFeInt.XmlNfe);
                    var chNFe = string.Empty;

                    if (conteudo.StartsWith("<resNFe"))
                    {
                        var retConteudo =
                            FuncoesXml.XmlStringParaClasse<Classes.Servicos.DistribuicaoDFe.Schemas.resNFe>(conteudo);
                        chNFe = retConteudo.chNFe;
                        dFeInt.ResNFe = retConteudo;
                    }
                    else if (conteudo.StartsWith("<procEventoNFe"))
                    {
                        var procEventoNFeConteudo =
                            FuncoesXml.XmlStringParaClasse<Classes.Servicos.DistribuicaoDFe.Schemas.procEventoNFe>(conteudo);
                        chNFe = procEventoNFeConteudo.retEvento.infEvento.chNFe;
                        dFeInt.ProcEventoNFe = procEventoNFeConteudo;
                    }
                    else if (conteudo.StartsWith("<resEvento"))
                    {
                        var resEventoConteudo =
                            FuncoesXml.XmlStringParaClasse<Classes.Servicos.DistribuicaoDFe.Schemas.resEvento>(conteudo);
                        chNFe = resEventoConteudo.chNFe;
                        dFeInt.ResEvento = resEventoConteudo;
                    }
                    else if (conteudo.StartsWith("<nfeProc"))
                    {
                        var resEventoConteudo =
                            FuncoesXml.XmlStringParaClasse<nfeProc>(conteudo);
                        chNFe = resEventoConteudo.protNFe.infProt.chNFe;
                        dFeInt.NfeProc = resEventoConteudo;
                    }

                    var schema = dFeInt.schema.Split('_');
                    if (chNFe == string.Empty)
                        chNFe = DateTime.Now.ParaDataHoraString() + "_SEMCHAVE";

                    SalvarArquivoXml(chNFe + "-" + schema[0] + ".xml", conteudo);
                }
            }

            #endregion

            return new RetornoNfeDistDFeInt(pedDistDFeInt.ObterXmlString(), retConsulta.ObterXmlString(), retornoXmlString, retConsulta);

            #endregion
        }

        #region Recepção

        /// <summary>
        ///     Envia uma ou mais NFe
        /// </summary>
        /// <param name="idLote"></param>
        /// <param name="nFes"></param>
        /// <returns>Retorna um objeto da classe RetornoNfeRecepcao com com os dados do resultado da transmissão</returns>
        public RetornoNfeRecepcao NfeRecepcao(int idLote, List<Classes.NFe> nFes)
        {
            var versaoServico = ServicoNFe.NfeRecepcao.VersaoServicoParaString(_cFgServico.VersaoNfeRecepcao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeRecepcao);

            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                versaoDados = versaoServico
            };

            #endregion

            #region Cria o objeto enviNFe

            var pedEnvio = new enviNFe2(versaoServico, idLote, nFes);

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlEnvio = pedEnvio.ObterXmlString();

            if (_cFgServico.cUF == Estado.PR)
                //Caso o lote seja enviado para o PR, colocar o namespace nos elementos <NFe> do lote, pois o serviço do PR o exige, conforme https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe/issues/33
                xmlEnvio = xmlEnvio.Replace("<NFe>", "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");

            SalvarArquivoXml(idLote + "-env-lot.xml", xmlEnvio);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeRecepcao, _cFgServico.VersaoNfeRecepcao, xmlEnvio, cfgServico: _cFgServico);

            var dadosEnvio = new XmlDocument();
            dadosEnvio.LoadXml(xmlEnvio);


            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosEnvio);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeRecepcao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retEnvio = new retEnviNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(idLote + "-rec.xml", retornoXmlString);

            return new RetornoNfeRecepcao(pedEnvio.ObterXmlString(), retEnvio.ObterXmlString(), retornoXmlString,
                retEnvio);

            #endregion
        }

        /// <summary>
        ///     Recebe o retorno do processamento de uma ou mais NFe's pela SEFAZ
        /// </summary>
        /// <param name="recibo"></param>
        /// <returns>Retorna um objeto da classe RetornoNfeRetRecepcao com com os dados do processamento do lote</returns>
        public RetornoNfeRetRecepcao NfeRetRecepcao(string recibo)
        {
            var versaoServico = ServicoNFe.NfeRetRecepcao.VersaoServicoParaString(_cFgServico.VersaoNfeRetRecepcao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NfeRetRecepcao);

            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                versaoDados = versaoServico
            };

            #endregion

            #region Cria o objeto consReciNFe

            var pedRecibo = new consReciNFe
            {
                versao = versaoServico,
                tpAmb = _cFgServico.tpAmb,
                nRec = recibo
            };

            #endregion

            #region Envia os dados e obtém a resposta

            var xmlRecibo = pedRecibo.ObterXmlString();
            var dadosRecibo = new XmlDocument();
            dadosRecibo.LoadXml(xmlRecibo);

            SalvarArquivoXml(recibo + "-ped-rec.xml", xmlRecibo);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosRecibo);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeRetRecepcao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retRecibo = new retConsReciNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(recibo + "-pro-rec.xml", retornoXmlString);

            return new RetornoNfeRetRecepcao(pedRecibo.ObterXmlString(), retRecibo.ObterXmlString(), retornoXmlString,
                retRecibo);

            #endregion
        }

        #endregion

        #region Autorização

        /// <summary>
        ///     Envia uma ou mais NFe
        /// </summary>
        /// <param name="idLote">ID do Lote</param>
        /// <param name="indSinc">Indicador de Sincronização</param>
        /// <param name="nFes">Lista de NFes a serem enviadas</param>
        /// <param name="compactarMensagem">Define se a mensagem será enviada para a SEFAZ compactada</param>
        /// <returns>Retorna um objeto da classe RetornoNFeAutorizacao com com os dados do resultado da transmissão</returns>
        public RetornoNFeAutorizacao NFeAutorizacao(int idLote, IndicadorSincronizacao indSinc, List<Classes.NFe> nFes,
            bool compactarMensagem = false)
        {
            if (_cFgServico.VersaoNFeAutorizacao != VersaoServico.Versao400)
            {
                return NFeAutorizacaoVersao310(idLote, indSinc, nFes, compactarMensagem);
            }

            if (_cFgServico.VersaoNFeAutorizacao == VersaoServico.Versao400)
            {
                return NFeAutorizacao4(idLote, indSinc, nFes, compactarMensagem);
            }

            throw new InvalidOperationException("Versão inválida");
        }

        private RetornoNFeAutorizacao NFeAutorizacao4(int idLote, IndicadorSincronizacao indSinc, List<Classes.NFe> nFes, bool compactarMensagem)
        {
            var versaoServico = ServicoNFe.NFeAutorizacao.VersaoServicoParaString(_cFgServico.VersaoNFeAutorizacao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServicoAutorizacao(ServicoNFe.NFeAutorizacao, compactarMensagem);

            #endregion

            #region Cria o objeto enviNFe

            var pedEnvio = new enviNFe4(versaoServico, idLote, indSinc, nFes);

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlEnvio = _cFgServico.RemoverAcentos
                ? pedEnvio.ObterXmlString().RemoverAcentos()
                : pedEnvio.ObterXmlString();

            if (_cFgServico.cUF == Estado.PR)
                //Caso o lote seja enviado para o PR, colocar o namespace nos elementos <NFe> do lote, pois o serviço do PR o exige, conforme https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe/issues/33
                xmlEnvio = xmlEnvio.Replace("<NFe>", "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");

            SalvarArquivoXml(idLote + "-env-lot.xml", xmlEnvio);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NFeAutorizacao, _cFgServico.VersaoNFeAutorizacao, xmlEnvio, cfgServico: _cFgServico);

            var dadosEnvio = new XmlDocument();
            dadosEnvio.LoadXml(xmlEnvio);

            XmlNode retorno;
            try
            {
                if (compactarMensagem)
                {
                    var xmlCompactado = Convert.ToBase64String(Compressao.Zip(xmlEnvio));
                    retorno = ws.ExecuteZip(xmlCompactado);
                }
                else
                {
                    retorno = ws.Execute(dadosEnvio);
                }
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NFeAutorizacao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retEnvio = new retEnviNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(idLote + "-rec.xml", retornoXmlString);

            return new RetornoNFeAutorizacao(xmlEnvio, retEnvio.ObterXmlString(), retornoXmlString, retEnvio);

            #endregion
        }

        private RetornoNFeAutorizacao NFeAutorizacaoVersao310(int idLote, IndicadorSincronizacao indSinc, List<Classes.NFe> nFes, bool compactarMensagem)
        {
            var versaoServico = ServicoNFe.NFeAutorizacao.VersaoServicoParaString(_cFgServico.VersaoNFeAutorizacao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServicoAutorizacao(ServicoNFe.NFeAutorizacao, compactarMensagem);

            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                versaoDados = versaoServico
            };

            #endregion

            #region Cria o objeto enviNFe

            var pedEnvio = new enviNFe3(versaoServico, idLote, indSinc, nFes);

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlEnvio = pedEnvio.ObterXmlString();
            if (_cFgServico.cUF == Estado.PR)
                //Caso o lote seja enviado para o PR, colocar o namespace nos elementos <NFe> do lote, pois o serviço do PR o exige, conforme https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe/issues/33
                xmlEnvio = xmlEnvio.Replace("<NFe>", "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");

            SalvarArquivoXml(idLote + "-env-lot.xml", xmlEnvio);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NFeAutorizacao, _cFgServico.VersaoNFeAutorizacao, xmlEnvio, cfgServico: _cFgServico);

            var dadosEnvio = new XmlDocument();
            dadosEnvio.LoadXml(xmlEnvio);

            XmlNode retorno;
            try
            {
                if (compactarMensagem)
                {
                    var xmlCompactado = Convert.ToBase64String(Compressao.Zip(xmlEnvio));
                    retorno = ws.ExecuteZip(xmlCompactado);
                }
                else
                {
                    retorno = ws.Execute(dadosEnvio);
                }
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NFeAutorizacao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retEnvio = new retEnviNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(idLote + "-rec.xml", retornoXmlString);

            return new RetornoNFeAutorizacao(pedEnvio.ObterXmlString(), retEnvio.ObterXmlString(), retornoXmlString, retEnvio);

            #endregion
        }

        /// <summary>
        ///     Recebe o retorno do processamento de uma ou mais NFe's pela SEFAZ
        /// </summary>
        /// <param name="recibo"></param>
        /// <returns>Retorna um objeto da classe RetornoNFeRetAutorizacao com com os dados do processamento do lote</returns>
        public RetornoNFeRetAutorizacao NFeRetAutorizacao(string recibo)
        {
            var versaoServico = ServicoNFe.NFeRetAutorizacao.VersaoServicoParaString(_cFgServico.VersaoNFeRetAutorizacao);

            #region Cria o objeto wdsl para consulta

            var ws = CriarServico(ServicoNFe.NFeRetAutorizacao);

            if (_cFgServico.VersaoNFeRetAutorizacao != VersaoServico.Versao400)
            {
                ws.nfeCabecMsg = new nfeCabecMsg
                {
                    cUF = _cFgServico.cUF,
                    versaoDados = versaoServico
                };
            }

            #endregion

            #region Cria o objeto consReciNFe

            var pedRecibo = new consReciNFe
            {
                versao = versaoServico,
                tpAmb = _cFgServico.tpAmb,
                nRec = recibo
            };

            #endregion

            #region Envia os dados e obtém a resposta

            var xmlRecibo = pedRecibo.ObterXmlString();
            var dadosRecibo = new XmlDocument();
            dadosRecibo.LoadXml(xmlRecibo);

            SalvarArquivoXml(recibo + "-ped-rec.xml", xmlRecibo);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosRecibo);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NFeRetAutorizacao, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retRecibo = new retConsReciNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(recibo + "-pro-rec.xml", retornoXmlString);

            return new RetornoNFeRetAutorizacao(pedRecibo.ObterXmlString(), retRecibo.ObterXmlString(), retornoXmlString, retRecibo);

            #endregion
        }

        #endregion

        /// <summary>
        ///     Consulta a Situação da NFe
        /// </summary>
        /// <returns>Retorna um objeto da classe RetornoNfeConsultaProtocolo com os dados da Situação da NFe</returns>
        [Obsolete("Descontinuado pela Sefaz")]
        public RetornoNfeDownload NfeDownloadNf(string cnpj, List<string> chaves, string nomeSaida = "")
        {
            var versaoServico = ServicoNFe.NfeDownloadNF.VersaoServicoParaString(_cFgServico.VersaoNfeDownloadNF);

            #region Cria o objeto wdsl para envio do pedido de Download

            var ws = CriarServico(ServicoNFe.NfeDownloadNF);

            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                //Embora em http://www.nfe.fazenda.gov.br/portal/webServices.aspx?tipoConteudo=Wak0FwB7dKs=#GO esse serviço está nas versões 2.00 e 3.10, ele rejeita se mandar a versão diferente de 1.00. Testado no Ambiente Nacional - (AN)
                versaoDados = /*versaoServico*/ "1.00"
            };

            #endregion

            #region Cria o objeto downloadNFe

            var pedDownload = new downloadNFe
            {
                //Embora em http://www.nfe.fazenda.gov.br/portal/webServices.aspx?tipoConteudo=Wak0FwB7dKs=#GO esse serviço está nas versões 2.00 e 3.10, ele rejeita se mandar a versão diferente de 1.00. Testado no Ambiente Nacional - (AN)
                versao = /*versaoServico*/ "1.00",
                CNPJ = cnpj,
                tpAmb = _cFgServico.tpAmb,
                chNFe = chaves
            };

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlDownload = pedDownload.ObterXmlString();

            if (nomeSaida == "")
            {
                nomeSaida = cnpj;
            }

            SalvarArquivoXml(nomeSaida + "-ped-down.xml", xmlDownload);

            if (_cFgServico.ValidarSchemas)
                Validador.Valida(ServicoNFe.NfeDownloadNF, _cFgServico.VersaoNfeDownloadNF, xmlDownload, cfgServico: _cFgServico);

            var dadosDownload = new XmlDocument();
            dadosDownload.LoadXml(xmlDownload);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosDownload);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfeDownloadNF, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retDownload = new retDownloadNFe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(nomeSaida + "-down.xml", retornoXmlString);

            return new RetornoNfeDownload(pedDownload.ObterXmlString(), retDownload.ObterXmlString(), retornoXmlString, retDownload);

            #endregion
        }

        #region Adm CSC

        public RetornoAdmCscNFCe AdmCscNFCe(string raizCnpj, IdentificadorOperacaoCsc identificadorOperacaoCsc, string idCscASerRevogado = null, string codigoCscASerRevogado = null)
        {
            var versaoServico = ServicoNFe.NfceAdministracaoCSC.VersaoServicoParaString(_cFgServico.VersaoNfceAministracaoCSC);

            #region Cria o objeto wdsl para envio do pedido de Download

            var ws = CriarServico(ServicoNFe.NfceAdministracaoCSC);

            ws.nfeCabecMsg = new nfeCabecMsg
            {
                cUF = _cFgServico.cUF,
                versaoDados = versaoServico
            };

            #endregion

            #region Cria o objeto downloadNFe

            var admCscNFCe = new admCscNFCe
            {
                versao = versaoServico,
                tpAmb = _cFgServico.tpAmb,
                indOp = identificadorOperacaoCsc,
                raizCNPJ = raizCnpj
            };

            if (identificadorOperacaoCsc == IdentificadorOperacaoCsc.ioRevogaCscAtivo)
            {
                admCscNFCe.dadosCsc = new dadosCsc
                {
                    codigoCsc = codigoCscASerRevogado,
                    idCsc = idCscASerRevogado
                };
            }

            #endregion

            #region Valida, Envia os dados e obtém a resposta

            var xmlAdmCscNfe = admCscNFCe.ObterXmlString();
            var dadosAdmnistracaoCsc = new XmlDocument();
            dadosAdmnistracaoCsc.LoadXml(xmlAdmCscNfe);

            SalvarArquivoXml(raizCnpj + "-adm-csc.xml", xmlAdmCscNfe);

            XmlNode retorno;
            try
            {
                retorno = ws.Execute(dadosAdmnistracaoCsc);
            }
            catch (WebException ex)
            {
                throw FabricaComunicacaoException.ObterException(ServicoNFe.NfceAdministracaoCSC, ex);
            }

            var retornoXmlString = retorno.OuterXml;
            var retCsc = new retAdmCscNFCe().CarregarDeXmlString(retornoXmlString);

            SalvarArquivoXml(raizCnpj + "-ret-adm-csc.xml", retornoXmlString);

            return new RetornoAdmCscNFCe(admCscNFCe.ObterXmlString(), retCsc.ObterXmlString(), retornoXmlString, retCsc);

            #endregion
        }

        #endregion


        #region Implementação do padrão Dispose

        // Flag: Dispose já foi chamado?
        private bool _disposed;

        // Implementação protegida do padrão Dispose.
        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                if (!_cFgServico.Certificado.ManterDadosEmCache && _controlarCertificado)
                    _certificado.Reset();
            _disposed = disposing;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ServicosNFe()
        {
            Dispose(false);
        }

        #endregion   
     }
}