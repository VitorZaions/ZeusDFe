using System.ComponentModel;
using System.Xml.Serialization;

namespace CTe.Classes.Servicos.Tipos
{
    public enum ServicoCTe
    {
        /// <summary>
        ///     serviço destinado à recepção de mensagem do Evento de Cancelamento da NF-e
        /// </summary>
        RecepcaoEventoCancelmento,

        /// <summary>
        ///     serviço destinado à recepção de mensagem do Evento de Carta de Correção da NF-e
        /// </summary>
        RecepcaoEventoCartaCorrecao,

        /// <summary>
        ///     serviço destinado à recepção de mensagem do Evento EPEC da NF-e
        /// </summary>
        RecepcaoEventoEpec,

        /// <summary>
        ///     serviço destinado à recepção de mensagem do Evento de Manifestação do destinatário da NF-e
        /// </summary>
        RecepcaoEventoManifestacaoDestinatario,

        /// <summary>
        ///     serviço destinado à recepção de mensagens de lote de NF-e versão 2.0
        /// </summary>
        CteRecepcao,

        /// <summary>
        ///     serviço destinado a retornar o resultado do processamento do lote de NF-e versão 2.0
        /// </summary>
        CteRetRecepcao,

        /// <summary>
        ///     Serviço para consultar o cadastro de contribuintes do ICMS da unidade federada
        /// </summary>
        CteConsultaCadastro,

        /// <summary>
        ///     serviço destinado ao atendimento de solicitações de inutilização de numeração
        /// </summary>
        CteInutilizacao,

        /// <summary>
        ///     serviço destinado ao atendimento de solicitações de consulta da situação atual da NF-e
        ///     na Base de Dados do Portal da Secretaria de Fazenda Estadual
        /// </summary>
        CteConsultaProtocolo,

        /// <summary>
        ///     serviço destinado à consulta do status do serviço prestado pelo Portal da Secretaria de Fazenda Estadual
        /// </summary>
        CteStatusServico,

        /// <summary>
        ///     serviço destinado à recepção de mensagens de lote de NF-e versão 3.10
        /// </summary>
        CteAutorizacao,

        /// <summary>
        ///     serviço destinado a retornar o resultado do processamento do lote de NF-e versão 3.10
        /// </summary>
        CteRetAutorizacao,

        /// <summary>
        ///     Distribui documentos e informações de interesse do ator da NF-e
        /// </summary>
        CteDistribuicaoDFe,

        /// <summary>
        ///     “Serviço de Consulta da Relação de Documentos Destinados” para um determinado CNPJ
        ///     de destinatário informado na NF-e.
        /// </summary>
        CteConsultaDest,

        /// <summary>
        ///     Serviço destinado ao atendimento de solicitações de download de Notas Fiscais Eletrônicas por seus destinatários
        /// </summary>
        CteDownloadNF
    }

    /// <summary>
    ///     Usado para discriminar o tipo de evento, pois o serviço de cancelamento e carta de correção devem usar a url
    ///     designada para UF da empresa, já o serviço EPEC usa a url do ambiente nacional
    /// </summary>
    public enum TipoRecepcaoEvento
    {
        Nenhum,
        Cancelamento,
        CartaCorrecao,
        Epec,
        ManifestacaoDestinatario
    }

    /// <summary>
    ///     Manifestação
    ///     <para>210200 - Confirmação da Operação;</para>
    ///     <para>210210 - Ciência da Operação;</para>
    ///     <para>210220 - Desconhecimento da Operação;</para>
    ///     <para>210240 - Operação não Realizada</para>
    /// </summary>
    public enum TipoManifestacao
    {
        /// <summary>
        /// 210200 - Confirmação da Operação
        /// </summary>
        [Description("Confirmação da Operação")]
        [XmlEnum("Confirmacao da Operacao")]
        e210200,

        /// <summary>
        /// 210210 - Ciência da Operação
        /// </summary>
        [Description("Ciência da Operação")]
        [XmlEnum("Ciencia da Operacao")]
        e210210,

        /// <summary>
        /// 210220 - Desconhecimento da Operação
        /// </summary>
        [Description("Desconhecimento da Operação")]
        [XmlEnum("Desconhecimento da Operacao")]
        e210220,

        /// <summary>
        /// 210240 - Operação não Realizada
        /// </summary>
        [Description("Operação não Realizada")]
        [XmlEnum("Operacao nao Realizada")]
        e210240
    }

    /// <summary>
    ///     Versão do leiaute
    ///     <para>2.00 - Versão 2.00;</para>
    ///     <para>3.00 - Versão 3.00/3.00a</para>
    ///     <para>4.00 - Versão 4.00</para>
    /// </summary>
    public enum versao
    {
        /// <summary>
        /// 2.00 - Versão 2.00
        /// </summary>
        [Description("Versão 2.00")]
        [XmlEnum("2.00")]
        ve200,

        /// <summary>
        /// 3.00 - Versão 3.00/3.00a
        /// </summary>
        [Description("Versão 3.00/3.00a")]
        [XmlEnum("3.00")]
        ve300,

        /// <summary>
        /// 4.00 - Versão 4.00
        /// </summary>
        [Description("Versão 4.00")]
        [XmlEnum("4.00")]
        ve400
    }

    /// <summary>
    ///     Indicador de Sincronização:
    ///     <para>0 - Assíncrono. A resposta deve ser obtida consultando o serviço NFeRetAutorizacao, com o nº do recibo</para>
    ///     <para>1 - Síncrono. Empresa solicita processamento síncrono do Lote de NF-e (sem a geração de Recibo para consulta futura);</para>
    /// </summary>
    public enum IndicadorSincronizacao
    {
        /// <summary>
        /// 0 - Assíncrono. A resposta deve ser obtida consultando o serviço NFeRetAutorizacao, com o nº do recibo
        /// </summary>
        [Description("Assíncrono. A resposta deve ser obtida consultando o serviço NFeRetAutorizacao, com o nº do recibo")]
        [XmlEnum("0")]
        Assincrono = 0,

        /// <summary>
        /// 1 - Síncrono. Empresa solicita processamento síncrono do Lote de NF-e (sem a geração de Recibo para consulta futura)
        /// </summary>
        [Description("Síncrono. Empresa solicita processamento síncrono do Lote de NF-e (sem a geração de Recibo para consulta futura)")]
        [XmlEnum("1")]
        Sincrono = 1
    }
}